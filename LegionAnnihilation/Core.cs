using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Ensage;
using Ensage.Common;
using Ensage.Common.Extensions;
using Ensage.Common.Menu;
using Ensage.Common.Objects;
using Ensage.Common.Objects.UtilityObjects;
using Ensage.Common.Threading;
using SharpDX;

namespace Legion_Annihilation
{
    public class Core
    {
        #region LittleMembers

        private static CancellationTokenSource _tks;
        private static Task _testCombo;
        private static readonly MultiSleeper ComboSleeper = new MultiSleeper();
        private static bool IsEnable => Members.Menu.Item("Enable").GetValue<bool>();
        private static bool OrbEnable => Members.Menu.Item("Orbwalking.Enable").GetValue<bool>();
        private static bool OrbFollow => Members.Menu.Item("Orbwalking.FollowTarget").GetValue<bool>();
        private static bool OrbInStun => Members.Menu.Item("Orbwalking.WhileTargetInStun").GetValue<bool>();
        private static bool UseHealBeforeInvis => Members.Menu.Item("UseHealBeforeInvis.Enable").GetValue<bool>();
        private static float InvisRange => Members.Menu.Item("InvisRange.value").GetValue<Slider>().Value;
        private static bool ComboKey => Members.Menu.Item("Combo.Enable").GetValue<KeyBind>().Active;

        #endregion

        #region Constructor

        public Core()
        {
            MenuManager.Init();
            Members.MyHero = ObjectManager.LocalHero;
            Members.MyTeam = ObjectManager.LocalHero.Team;
            GameDispatcher.OnUpdate += GameDispatcherOnOnUpdate;
            Game.OnUpdate += UpdateItems;
            Members.Updater = new Sleeper();
            Printer.Print($"{Members.Menu.DisplayName} loaded! v.[{Assembly.GetExecutingAssembly().GetName().Version}]",
                true, MessageType.LogMessage);
        }

        #endregion

        #region MainMethod

        public static void OnValueChanged(object sender, OnValueChangeEventArgs args)
        {
            var oldOne = args.GetOldValue<KeyBind>().Active;
            var newOne = args.GetNewValue<KeyBind>().Active;
            if (oldOne == newOne || newOne) return;
            try
            {
                _tks.Cancel();
                _globalTarget = null;
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private static Hero _globalTarget;
        private static async Task Action(CancellationToken cancellationToken)
        {
            if (_globalTarget == null || !_globalTarget.IsValid || !_globalTarget.IsAlive)
            {
                _globalTarget = TargetSelector.ClosestToMouse(Members.MyHero);
                return;
            }
            var target = _globalTarget;
            if (!Members.MyHero.IsInvisible() &&
                !Members.MyHero.HasModifiers(new[]
                {"modifier_item_invisibility_edge_windwalk", "modifier_item_silver_edge_windwalk"}) && 
                !ComboSleeper.Sleeping("invisAction"))
            {
                if (Members.MyHero.FindItem("item_blink", true) != null)
                    await UseBlink(target, cancellationToken);
                else if (Members.MyHero.FindItem("item_invis_sword", true) != null ||
                         Members.MyHero.FindItem("item_silver_edge", true) != null)
                    await UseInvis(target, cancellationToken);
                //await UseAbility(new Ability(), Target, cancellationToken);

                var inventory =
                    Members.MyHero.Inventory.Items.Where(
                        x =>
                            x.CanBeCasted() &&
                            !(x.TargetTeamType == TargetTeamType.Enemy || x.TargetTeamType == TargetTeamType.All ||
                              x.TargetTeamType == TargetTeamType.Custom) ||
                            x.CanHit(target));
                var enumerable = inventory as Item[] ?? inventory.ToArray();

                var linkerBreakers =
                    enumerable.Where(
                        x =>
                            Helper.IsItemEnable(x, false) && target.IsLinkensProtected() && !ComboSleeper.Sleeping(x));

                foreach (var item in linkerBreakers)
                {
                    await UseItem(item, target, cancellationToken);
                }
                var itemInCombo =
                    enumerable.Where(
                        x => Helper.IsItemEnable(x) && !target.IsLinkensProtected() && !ComboSleeper.Sleeping(x));
                foreach (var item in itemInCombo)
                {
                    await UseItem(item, target, cancellationToken);
                }

                var ult = Members.MyHero.FindSpell(Members.AbilityList[2]);
                if (ult.CanBeCasted() && !target.IsLinkensProtected())
                {
                    if (ult.CanHit(target))
                    {
                        ult.UseAbility(target);
                        await Task.Delay(300, cancellationToken);
                    }
                }
            }
            if (OrbEnable && (!target.IsStunned() || !OrbInStun))
            {
                Orbwalking.Orbwalk(target, followTarget: OrbFollow);
            }
            else if (Utils.SleepCheck("attack_rate"))
            {
                if (!Members.MyHero.IsAttacking())
                {
                    Members.MyHero.Attack(target);
                    Utils.Sleep(125, "attack_rate");
                }
            }
        }

        private static async Task UseInvis(Hero target, CancellationToken cancellationToken)
        {
            while (true)
            {
                var invis = Members.MyHero.FindItem("item_invis_sword", true) ??
                            Members.MyHero.FindItem("item_silver_edge", true);
                var dist = Members.MyHero.Distance2D(target);
                if (UseHealBeforeInvis && dist <= InvisRange)
                {
                    await UseHeal(cancellationToken);
                }
                if (dist <= InvisRange)
                {
                    if (invis.CanBeCasted())
                    {
                        Printer.Print("inv");
                        invis.UseAbility();
                        await Task.Delay(5, cancellationToken);
                        ComboSleeper.Sleep(1000, "invisAction");
                    }
                    else
                    {
                        if (!UseHealBeforeInvis)
                        {
                            await UseHeal(cancellationToken);
                        }
                    }
                }
                else
                {
                    if (Utils.SleepCheck("move_rate"))
                    {
                        Members.MyHero.Move(target.Position);
                        Utils.Sleep(125, "move_rate");
                    }
                    await Task.Delay(5, cancellationToken);
                    continue;
                }
                break;
            }
        }



        private static async Task UseBlink(Unit target, CancellationToken cancellationToken)
        {
            while (true)
            {
                var position = target.Position;
                var point = new Vector3(
                    (float)
                        (position.X - 50*Math.Cos(Members.MyHero.FindAngleBetween(position, true))),
                    (float)
                        (position.Y - 50*Math.Sin(Members.MyHero.FindAngleBetween(position, true))),
                    0);
                position = point;
                var blink = Members.MyHero.FindItem("item_blink");
                
                var dist = Members.MyHero.Distance2D(position);
                var blinkIsRdy = blink != null && blink.CanBeCasted();
                if ((dist <= 1180 && blinkIsRdy) || (dist <= 450 && !blinkIsRdy))
                {
                    await UseHeal(cancellationToken);
                    /*if (Helper.IsAbilityEnable(heal))
                    {
                        heal.UseAbility(Members.MyHero);
                        await Task.Delay(200, cancellationToken);
                    }*/
                }
                if (blinkIsRdy)
                {
                    if (dist <= 1180)
                    {
                        blink.UseAbility(position);
                        await Task.Delay(50, cancellationToken);
                    }
                    else 
                    {
                        if (Utils.SleepCheck("move_rate"))
                        {
                            Members.MyHero.Move(position);
                            //await Task.Delay(125, cancellationToken);
                            Utils.Sleep(125, "move_rate");
                        }
                        await Task.Delay(5, cancellationToken);
                        continue;
                    }
                }
                break;
            }
        }

        #endregion

        #region Helpers

        private static async Task UseHeal(CancellationToken cancellationToken)
        {
            var heal = Members.MyHero.FindSpell(Members.AbilityList[1]);
            if (heal.CanBeCasted() && Helper.IsAbilityEnable(heal))
            {
                heal.UseAbility(Members.MyHero);
                await Task.Delay((int) (200+Game.Ping), cancellationToken);
            }
        }

        private static async Task UseItem(Ability ability, Unit target, CancellationToken cancellationToken)
        {
            if (ComboSleeper.Sleeping("invisAction"))
                return;
            ComboSleeper.Sleep(150, ability);
            if (ability.StoredName() == "item_armlet")
            {
                if (!Members.MyHero.HasModifier("modifier_item_armlet_unholy_strength"))
                {
                    ability.ToggleAbility();
                }
                else
                {
                    return;
                }
            }
            else if (ability.IsAbilityBehavior(AbilityBehavior.NoTarget))
            {
                ability.UseAbility();
            }
            else if (ability.IsAbilityBehavior(AbilityBehavior.UnitTarget))
            {
                if (ability.TargetTeamType == TargetTeamType.Enemy || ability.TargetTeamType == TargetTeamType.All ||
                    ability.TargetTeamType == TargetTeamType.Custom)
                {
                    ability.UseAbility(target);
                }
                else
                {
                    ability.UseAbility(Members.MyHero);
                }
            }
            Printer.Print($"[{(int) Game.RawGameTime}] [Item] {ability.Name}: {50}");
            await Task.Delay(5, cancellationToken);
        }

        #endregion

        #region Updaters

        public static async void GameDispatcherOnOnUpdate(EventArgs args)
        {
            if (!IsEnable)
                return;
            if (_testCombo != null && !_testCombo.IsCompleted)
                return;
            if (ComboKey)
            {
                _tks = new CancellationTokenSource();
                _testCombo = Action(_tks.Token);
                try
                {
                    await _testCombo;
                    _testCombo = null;
                }
                catch (OperationCanceledException)
                {
                    _globalTarget = null;
                    _testCombo = null;
                    Printer.Print("cancel");
                }
            }
            /*if (_myLittleCombo != null)
            {
                await _myLittleCombo.Execute();
            }*/
        }

        public static void UpdateItems(EventArgs args)
        {
            if (!IsEnable)
                return;
            if (Members.Updater.Sleeping)
                return;
            Members.Updater.Sleep(500);
            var inventory = Members.MyHero.Inventory.Items;
            var enumerable = inventory as IList<Item> ?? inventory.ToList();
            var neededItems = enumerable.Where(item => !Members.BlackList.Contains(item.StoredName()) && !Members.Items.Contains(item.StoredName()) &&
                                                       (item.IsDisable() || item.IsNuke() || item.IsPurge()
                                                        || item.IsSilence() || item.IsShield() ||
                                                        item.IsSlow() || item.IsSkillShot() ||
                                                        Members.WhiteList.Contains(item.StoredName())));
            foreach (var item in neededItems)
            {
                Members.Items.Add(item.StoredName());
                Members.Menu.Item("itemEnable")
                    .GetValue<AbilityToggler>().Add(item.StoredName());
                if (item.TargetTeamType == TargetTeamType.Enemy || item.TargetTeamType == TargetTeamType.All ||
                    item.TargetTeamType == TargetTeamType.Custom)
                    Members.Menu.Item("itemEnableLinken")
                        .GetValue<AbilityToggler>().Add(item.StoredName());
                Printer.Print($"[NewItem]: {item.StoredName()} || {item.TargetTeamType}");

            }
            var tempList = enumerable.Select(neededItem => neededItem.StoredName()).ToList();
            var removeList = new List<string>();
            foreach (var item in Members.Items.Where(x => !tempList.Contains(x)))
            {
                Members.Menu.Item("itemEnable")
                    .GetValue<AbilityToggler>().Remove(item);
                try
                {
                    Members.Menu.Item("itemEnableLinken")
                        .GetValue<AbilityToggler>().Remove(item);
                }
                catch
                {
                    // ignored
                }
                removeList.Add(item);
                Printer.Print($"[RemoveItem]: {item}");
            }
            foreach (var item in removeList)
            {
                Members.Items.Remove(item);
            }
        }

        #endregion
    }
}