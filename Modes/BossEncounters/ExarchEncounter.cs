using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using AutoExile.Systems;
using System.Numerics;

namespace AutoExile.Modes.BossEncounters
{
    /// <summary>
    /// "The Searing Exarch" (Incandescent Invitation) boss encounter.
    /// Area: Absence of Patience and Wisdom
    /// 
    /// Flow:
    ///   1. Enter arena → walk to center (252, 252) → fight Searing Exarch
    ///   2. Ball Phase / Rolling Meteor: Boss becomes untargetable/hidden -> hold position / dodge
    ///   3. Boss reappears -> continue fighting until death
    ///   4. Boss dies -> loot sweep (Forbidden Flame, Eldritch Orbs, Omniscience) -> exit to hideout
    /// 
    /// Fragment: Incandescent Invitation (CleansingBossFragment)
    /// Boss Entity: Metadata/Monsters/AtlasInvaders/CleansingMonsters/CleansingBoss@84
    /// </summary>
    public class ExarchEncounter : IBossEncounter
    {
        public string Name => "The Searing Exarch";
        public string Status { get; private set; } = "";

        // Fragment metadata path substrings
        private const string FragmentPath1 = "CleansingBossFragment";
        private const string FragmentPath2 = "Incandescent";
        private const string FragmentPath3 = "Cleansing";

        public Func<Element, bool> MapFilter => el =>
        {
            var entity = el.Entity;
            if (entity?.Path == null) return false;
            return entity.Path.Contains(FragmentPath1) || 
                   entity.Path.Contains(FragmentPath2) || 
                   entity.Path.Contains(FragmentPath3);
        };

        public string? InventoryFragmentPath => FragmentPath3;

        // Key high-value drops from The Searing Exarch
        public IReadOnlyList<string> MustLootItems { get; } = new[]
        {
            "Forbidden Flame",
            "Crystallised Omniscience",
            "Exceptional Eldritch Ichor",
            "Grand Eldritch Ichor",
            "Greater Eldritch Ichor",
            "Eldritch Chaos Orb",
            "Eldritch Exalted Orb",
            "Eldritch Orb of Annulment",
            "Orb of Conflict",
            "The Annihilating Light",
            "Dawnbreaker",
            "Incandescent Invitation",
            "Curio Item"
        };

        public bool SuppressCombatPositioning => _phase == ExarchPhase.BallPhase;
        public bool RelaxedPathing => false;

        // Verified Boss Metadata Path from DevTree
        private const string BossPath = "CleansingMonsters/CleansingBoss";

        // Arena Center position from in-game inspection
        private static readonly Vector2 ArenaCenterPos = new(252f, 252f);

        // State Machine
        private ExarchPhase _phase = ExarchPhase.Idle;
        private DateTime _phaseStartTime;
        private Entity? _bossEntity;
        private Vector2? _bossDeathPos;
        private DateTime _lastLootScan;
        private bool _bossWasAlive;
        private int _exploreFails;
        private Vector2 _lastPlayerGrid;

        private enum ExarchPhase
        {
            Idle,
            NavigateToCenter,
            Fighting,
            BallPhase,
            WaitingForLoot
        }

        public void OnEnterZone(BotContext ctx)
        {
            var gc = ctx.Game;
            InitExploration(ctx, gc);

            _phase = ExarchPhase.NavigateToCenter;
            _phaseStartTime = DateTime.Now;
            _bossEntity = null;
            _bossWasAlive = false;
            _exploreFails = 0;
            _lastPlayerGrid = new Vector2(gc.Player.GridPosNum.X, gc.Player.GridPosNum.Y);
            Status = "Entered arena — moving to Searing Exarch";
            ctx.Log($"[Exarch] Zone entered at ({_lastPlayerGrid.X:F0}, {_lastPlayerGrid.Y:F0})");
        }

        public BossEncounterResult Tick(BotContext ctx)
        {
            var gc = ctx.Game;
            if (gc?.Player == null) return BossEncounterResult.InProgress;

            var playerGrid = new Vector2(gc.Player.GridPosNum.X, gc.Player.GridPosNum.Y);
            _lastPlayerGrid = playerGrid;

            ctx.Exploration.Update(playerGrid);

            // Scan for Boss Entity
            if (_phase != ExarchPhase.WaitingForLoot)
            {
                _bossEntity = FindBoss(gc);

                if (_bossEntity != null)
                {
                    if (_bossEntity.IsAlive)
                        _bossWasAlive = true;

                    // Detect Boss Death
                    if (_bossWasAlive && (!_bossEntity.IsAlive || _bossEntity.IsDead))
                    {
                        _phase = ExarchPhase.WaitingForLoot;
                        _phaseStartTime = DateTime.Now;
                        _bossDeathPos = new Vector2(_bossEntity.GridPosNum.X, _bossEntity.GridPosNum.Y);
                        Status = "Searing Exarch defeated — sweeping loot";
                        ctx.Log("[Exarch] Boss killed, waiting for loot drops");
                        return BossEncounterResult.InProgress;
                    }
                }
            }

            switch (_phase)
            {
                case ExarchPhase.NavigateToCenter:
                    return TickNavigateToCenter(ctx, gc, playerGrid);
                case ExarchPhase.Fighting:
                    return TickFighting(ctx, gc, playerGrid);
                case ExarchPhase.BallPhase:
                    return TickBallPhase(ctx, gc, playerGrid);
                case ExarchPhase.WaitingForLoot:
                    return TickWaitingForLoot(ctx, gc, playerGrid);
                default:
                    return BossEncounterResult.InProgress;
            }
        }

        private BossEncounterResult TickNavigateToCenter(BotContext ctx, GameController gc, Vector2 playerGrid)
        {
            if ((DateTime.Now - _phaseStartTime).TotalSeconds > 60)
            {
                Status = "Timeout navigating to center";
                return BossEncounterResult.Failed;
            }

            if (_bossEntity != null && _bossEntity.IsAlive && _bossEntity.IsTargetable)
            {
                _phase = ExarchPhase.Fighting;
                _phaseStartTime = DateTime.Now;
                ctx.Log($"[Exarch] Boss engaged: {_bossEntity.RenderName}");
                return BossEncounterResult.InProgress;
            }

            var distToCenter = Vector2.Distance(playerGrid, ArenaCenterPos);
            if (distToCenter > 12 && !ctx.Navigation.IsNavigating)
            {
                if (!ctx.Navigation.NavigateTo(gc, ArenaCenterPos))
                {
                    _exploreFails++;
                    if (_exploreFails > 10)
                        return BossEncounterResult.Failed;
                }
            }

            Status = $"Moving to Exarch ({distToCenter:F0}g)";
            return BossEncounterResult.InProgress;
        }

        private BossEncounterResult TickFighting(BotContext ctx, GameController gc, Vector2 playerGrid)
        {
            if ((DateTime.Now - _phaseStartTime).TotalSeconds > 240)
            {
                Status = "Fight timeout";
                return BossEncounterResult.Failed;
            }

            if (_bossEntity != null && _bossEntity.IsAlive)
            {
                // Check if boss entered Ball Phase (Untargetable / Hidden)
                if (!_bossEntity.IsTargetable || _bossEntity.IsHidden)
                {
                    _phase = ExarchPhase.BallPhase;
                    _phaseStartTime = DateTime.Now;
                    Status = "Exarch Ball Phase — dodging meteors";
                    ctx.Log("[Exarch] Boss became untargetable, entering Ball Phase");
                    return BossEncounterResult.InProgress;
                }

                var bossGrid = new Vector2(_bossEntity.GridPosNum.X, _bossEntity.GridPosNum.Y);
                _bossDeathPos = bossGrid;
                var dist = Vector2.Distance(playerGrid, bossGrid);

                if (dist > ctx.Settings.Build.CombatRange.Value && !ctx.Navigation.IsNavigating)
                    ctx.Navigation.NavigateTo(gc, bossGrid);

                Status = $"Fighting The Searing Exarch — dist={dist:F0}";
            }
            else
            {
                Status = "Fighting — waiting for Exarch";
            }

            return BossEncounterResult.InProgress;
        }

        private BossEncounterResult TickBallPhase(BotContext ctx, GameController gc, Vector2 playerGrid)
        {
            if ((DateTime.Now - _phaseStartTime).TotalSeconds > 45)
            {
                // Ball phase ended by timeout -> return to fighting
                _phase = ExarchPhase.Fighting;
                _phaseStartTime = DateTime.Now;
                return BossEncounterResult.InProgress;
            }

            // If boss became targetable again -> resume fight
            if (_bossEntity != null && _bossEntity.IsAlive && _bossEntity.IsTargetable && !_bossEntity.IsHidden)
            {
                _phase = ExarchPhase.Fighting;
                _phaseStartTime = DateTime.Now;
                ctx.Log("[Exarch] Ball Phase ended, resuming combat");
                return BossEncounterResult.InProgress;
            }

            // Hold position / dodge
            ctx.Navigation.Stop(gc);
            Status = $"Ball Phase active ({(45 - (DateTime.Now - _phaseStartTime).TotalSeconds):F0}s remaining)";
            return BossEncounterResult.InProgress;
        }

        private BossEncounterResult TickWaitingForLoot(BotContext ctx, GameController gc, Vector2 playerGrid)
        {
            var timeout = ctx.Settings.Run.LootSweepTimeoutSeconds.Value;
            var elapsed = (DateTime.Now - _phaseStartTime).TotalSeconds;

            if (elapsed > timeout)
            {
                ctx.Log("[Exarch] Loot sweep finished — signaling Complete");
                return BossEncounterResult.Complete;
            }

            var remaining = timeout - elapsed;
            var countdown = $"({remaining:F0}s left)";

            if (_bossDeathPos.HasValue)
            {
                var distToLoot = Vector2.Distance(playerGrid, _bossDeathPos.Value);
                if (distToLoot > 15 && !ctx.Navigation.IsNavigating)
                    ctx.Navigation.NavigateTo(gc, _bossDeathPos.Value);
            }

            if ((DateTime.Now - _lastLootScan).TotalMilliseconds >= 500)
            {
                ctx.Loot.Scan(gc);
                _lastLootScan = DateTime.Now;
            }

            if (ctx.Interaction.IsBusy)
            {
                Status = $"Picking up loot {countdown}";
                return BossEncounterResult.InProgress;
            }

            if (ctx.Loot.HasLootNearby)
            {
                var (_, candidate) = ctx.Loot.PickupNext(ctx.Interaction, ctx.Navigation);
                if (candidate != null)
                {
                    Status = $"Looting: {candidate.ItemName} {countdown}";
                    return BossEncounterResult.InProgress;
                }
            }

            if (ctx.Loot.TogglePhase != LootSystem.LabelTogglePhase.Idle)
            {
                ctx.Loot.TickLabelToggle(gc);
                return BossEncounterResult.InProgress;
            }
            if (ctx.Loot.ShouldToggleLabels(gc))
            {
                ctx.Loot.StartLabelToggle(gc);
                return BossEncounterResult.InProgress;
            }

            Status = $"Waiting for loot {countdown}";
            return BossEncounterResult.InProgress;
        }

        private Entity? FindBoss(GameController gc)
        {
            foreach (var entity in gc.EntityListWrapper.ValidEntitiesByType[EntityType.Monster])
            {
                if (!entity.IsHostile) continue;
                if (entity.Rarity != MonsterRarity.Unique) continue;

                if (entity.Path.Contains(BossPath))
                    return entity;
            }
            return null;
        }

        private void InitExploration(BotContext ctx, GameController gc)
        {
            var pfGrid = gc.IngameState?.Data?.RawPathfindingData;
            var tgtGrid = gc.IngameState?.Data?.RawTerrainTargetingData;
            if (pfGrid != null && gc.Player != null)
            {
                var playerGrid = new Vector2(gc.Player.GridPosNum.X, gc.Player.GridPosNum.Y);
                ctx.Exploration.Initialize(pfGrid, tgtGrid, playerGrid,
                    ctx.Settings.Build.BlinkRange.Value);
            }
        }

        public void Render(BotContext ctx)
        {
            var gc = ctx.Game;
            var g = ctx.Graphics;
            if (gc?.Player == null || g == null) return;

            var cam = gc.IngameState.Camera;
            var playerGrid = new Vector2(gc.Player.GridPosNum.X, gc.Player.GridPosNum.Y);

            // Boss marker
            if (_bossEntity != null)
            {
                var world = _bossEntity.BoundsCenterPosNum;
                var screen = cam.WorldToScreen(world);
                if (screen.X > -200 && screen.X < 2400)
                {
                    var color = _bossEntity.IsAlive ? SharpDX.Color.Red : SharpDX.Color.LimeGreen;
                    g.DrawText(_bossEntity.IsAlive ? "THE SEARING EXARCH" : "EXARCH (DEAD)",
                        screen + new Vector2(-40, -30), color);
                }
            }

            // HUD
            float hudX = 20, hudY = 250, lineH = 18;
            var phaseColor = _phase switch
            {
                ExarchPhase.Fighting => SharpDX.Color.Red,
                ExarchPhase.BallPhase => SharpDX.Color.OrangeRed,
                ExarchPhase.WaitingForLoot => SharpDX.Color.LimeGreen,
                _ => SharpDX.Color.White,
            };
            g.DrawText($"Exarch: {_phase}", new Vector2(hudX, hudY), phaseColor);
            hudY += lineH;
            g.DrawText(Status, new Vector2(hudX, hudY), SharpDX.Color.Gray);
            hudY += lineH;
            g.DrawText($"Player: ({playerGrid.X:F0}, {playerGrid.Y:F0})", new Vector2(hudX, hudY), SharpDX.Color.DarkGray);
        }

        public void Reset()
        {
            _phase = ExarchPhase.Idle;
            _bossEntity = null;
            _bossWasAlive = false;
            _exploreFails = 0;
            _lastPlayerGrid = Vector2.Zero;
            _bossDeathPos = null;
            _lastLootScan = DateTime.MinValue;
            Status = "";
        }
    }
}
