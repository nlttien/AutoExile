using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.MemoryObjects;
using Life = ExileCore.PoEMemory.Components.Life;
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
            "Crimson Jewel",
            "Crystallised Omniscience",
            "Onyx Amulet",
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
            "Curio Item",
            "Jewel"
        };

        public bool SuppressCombat => _phase == ExarchPhase.WaitingForLoot;
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

        private bool _hasEngagedBoss;
        private DateTime _bossLastSeenAliveTime = DateTime.MinValue;
        private DateTime _combatStartTime = DateTime.MinValue;

        public void OnEnterZone(BotContext ctx)
        {
            var gc = ctx.Game;
            InitExploration(ctx, gc);

            _phase = ExarchPhase.NavigateToCenter;
            _phaseStartTime = DateTime.Now;
            _combatStartTime = DateTime.MinValue;
            _bossLastSeenAliveTime = DateTime.MinValue;
            _bossEntity = null;
            _bossWasAlive = false;
            _hasEngagedBoss = false;
            _exploreFails = 0;
            _lastPlayerGrid = new Vector2(gc.Player.GridPosNum.X, gc.Player.GridPosNum.Y);
            Status = "Entered arena — moving close to Searing Exarch (12 units range)";
            ctx.Log($"[Exarch] Zone entered at ({_lastPlayerGrid.X:F0}, {_lastPlayerGrid.Y:F0}) — moving close to boss");
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
                    var life = _bossEntity.GetComponent<Life>();
                    if (life != null && life.CurHP > 0)
                    {
                        _bossWasAlive = true;
                        _bossLastSeenAliveTime = DateTime.Now;
                        _bossDeathPos = new Vector2(_bossEntity.GridPosNum.X, _bossEntity.GridPosNum.Y);
                    }
                }
            }

            switch (_phase)
            {
                case ExarchPhase.NavigateToCenter:
                    return TickNavigateToCenter(ctx, gc, playerGrid);
                case ExarchPhase.Fighting:
                case ExarchPhase.BallPhase:
                    return TickFighting(ctx, gc, playerGrid);
                case ExarchPhase.WaitingForLoot:
                    return TickWaitingForLoot(ctx, gc, playerGrid);
                default:
                    return BossEncounterResult.InProgress;
            }
        }

        private const float CombatCloseRange = 12f;

        private BossEncounterResult TickNavigateToCenter(BotContext ctx, GameController gc, Vector2 playerGrid)
        {
            if ((DateTime.Now - _phaseStartTime).TotalSeconds > 60)
            {
                BotInput.ReleaseRightClick();
                Status = "Timeout navigating to center";
                return BossEncounterResult.Failed;
            }

            var targetGrid = _bossEntity != null
                ? new Vector2(_bossEntity.GridPosNum.X, _bossEntity.GridPosNum.Y)
                : ArenaCenterPos;

            var distToTarget = Vector2.Distance(playerGrid, targetGrid);

            // Calculate target screen position for pre-casting
            var cam = gc.IngameState.Camera;
            var targetWorld = _bossEntity != null
                ? _bossEntity.BoundsCenterPosNum
                : Pathfinding.GridToWorld3D(gc, targetGrid);
            var centerScreen = cam.WorldToScreen(targetWorld);
            var windowRect = gc.Window.GetWindowRectangle(); 
            var targetScreenPos = new Vector2(windowRect.X + centerScreen.X, windowRect.Y + centerScreen.Y);

            // Pre-cast skills onto boss spawn point as we get closer (<= 45 units) before boss appears
            if (distToTarget <= 45)
            {
                CastMainSkill(ctx, targetScreenPos);
            }

            // Move to close range (<= 12 units) before stopping to fight
            if (distToTarget <= CombatCloseRange)
            {
                ctx.Navigation.Stop(gc);
                _phase = ExarchPhase.Fighting;
                _phaseStartTime = DateTime.Now;
                _combatStartTime = DateTime.Now;
                _hasEngagedBoss = true;
                ctx.Log($"[Exarch] Reached close combat position ({distToTarget:F0}g <= {CombatCloseRange:F0}g) — engaging Searing Exarch");
                return BossEncounterResult.InProgress;
            }

            // Navigate directly to boss/arena center
            if (!ctx.Navigation.IsNavigating)
            {
                if (!ctx.Navigation.NavigateTo(gc, targetGrid))
                {
                    _exploreFails++;
                    if (_exploreFails > 10)
                    {
                        BotInput.ReleaseRightClick();
                        return BossEncounterResult.Failed;
                    }
                }
            }

            Status = distToTarget <= 45
                ? $"Pre-casting skills & advancing ({distToTarget:F0}g away)"
                : $"Moving close to Searing Exarch ({distToTarget:F0}g away, target: <= {CombatCloseRange:F0}g)";
            return BossEncounterResult.InProgress;
        }

        private BossEncounterResult TickFighting(BotContext ctx, GameController gc, Vector2 playerGrid)
        {
            if ((DateTime.Now - _phaseStartTime).TotalSeconds > 240)
            {
                BotInput.ReleaseRightClick();
                Status = "Fight timeout";
                return BossEncounterResult.Failed;
            }

            var targetGrid = _bossEntity != null
                ? new Vector2(_bossEntity.GridPosNum.X, _bossEntity.GridPosNum.Y)
                : ArenaCenterPos;

            var distToTarget = Vector2.Distance(playerGrid, targetGrid);

            // Check if boss is dead: only when CurHP <= 0 or IsDead is true
            bool isBossDead = false;
            if (_bossEntity != null)
            {
                var life = _bossEntity.GetComponent<Life>();
                if (_bossEntity.IsDead || (life != null && life.CurHP <= 0 && _bossWasAlive))
                {
                    isBossDead = true;
                }
            }
            else if (_bossWasAlive && _bossLastSeenAliveTime != DateTime.MinValue &&
                     (DateTime.Now - _bossLastSeenAliveTime).TotalSeconds > 5.0 &&
                     (DateTime.Now - _combatStartTime).TotalSeconds > 5.0)
            {
                // Boss entity despawned after active combat
                isBossDead = true;
            }

            if (isBossDead)
            {
                BotInput.ReleaseRightClick();
                _phase = ExarchPhase.WaitingForLoot;
                _phaseStartTime = DateTime.Now;
                _bossDeathPos ??= ArenaCenterPos;
                Status = "Searing Exarch defeated — sweeping loot";
                ctx.Log("[Exarch] Boss confirmed dead, switching to loot phase");
                return BossEncounterResult.InProgress;
            }

            // Determine target screen position (Boss position or Arena Center)
            var cam = gc.IngameState.Camera;
            Vector2 targetScreenPos;

            if (_bossEntity != null)
            {
                var bossWorld = _bossEntity.BoundsCenterPosNum;
                var bossScreen = cam.WorldToScreen(bossWorld);
                var windowRect = gc.Window.GetWindowRectangle();
                targetScreenPos = new Vector2(windowRect.X + bossScreen.X, windowRect.Y + bossScreen.Y);
            }
            else
            {
                var centerWorld = Pathfinding.GridToWorld3D(gc, targetGrid);
                var centerScreen = cam.WorldToScreen(centerWorld);
                var windowRect = gc.Window.GetWindowRectangle(); 
                targetScreenPos = new Vector2(windowRect.X + centerScreen.X, windowRect.Y + centerScreen.Y);
            }

            // Continuously cast attack/spell/trap skills on Boss non-stop until boss is dead
            CastMainSkill(ctx, targetScreenPos);

            // Stand still if within close range, otherwise step closer
            if (distToTarget > CombatCloseRange)
            {
                if (!ctx.Navigation.IsNavigating)
                    ctx.Navigation.NavigateTo(gc, targetGrid);
                Status = $"Casting & closing in ({distToTarget:F0}g > {CombatCloseRange:F0}g)";
            }
            else
            {
                ctx.Navigation.Stop(gc);
                var life = _bossEntity?.GetComponent<Life>();
                var hpText = life != null && life.MaxHP > 0
                    ? $" [{(float)life.CurHP / life.MaxHP * 100f:F0}% HP]"
                    : "";
                Status = $"Standing close ({distToTarget:F0}g) & casting at Searing Exarch{hpText}";
            }

            return BossEncounterResult.InProgress;
        }

        private static DateTime _lastCastTime = DateTime.MinValue;

        private static void CastMainSkill(BotContext ctx, Vector2 targetScreenPos)
        {
            if ((DateTime.Now - _lastCastTime).TotalMilliseconds < 100)
                return;
            _lastCastTime = DateTime.Now;

            // Move cursor to boss screen position
            if (BotInput.ClampToWindow(ref targetScreenPos))
            {
                Input.SetCursorPos(targetScreenPos);
            }

            var enemySkills = ctx.Settings.Build.AllSkillSlots
                .Where(s => s.Key.Value != System.Windows.Forms.Keys.None && s.Role.Value == SkillRole.Enemy.ToString())
                .OrderByDescending(s => s.Priority.Value)
                .ToList();

            if (enemySkills.Count > 0)
            {
                foreach (var s in enemySkills)
                {
                    var key = s.Key.Value;
                    if (key == System.Windows.Forms.Keys.RButton)
                    {
                        Input.RightDown();
                        Task.Delay(35).ContinueWith(_ => Input.RightUp());
                    }
                    else
                    {
                        Input.KeyDown(key);
                        Task.Delay(35).ContinueWith(_ => Input.KeyUp(key));
                    }
                }
            }
            else
            {
                // Default fallback: Right Click
                Input.RightDown();
                Task.Delay(35).ContinueWith(_ => Input.RightUp());
            }
        }

        private BossEncounterResult TickBallPhase(BotContext ctx, GameController gc, Vector2 playerGrid)
        {
            BotInput.ReleaseRightClick();

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

            var distToCenter = Vector2.Distance(playerGrid, ArenaCenterPos);
            if (distToCenter > 30 && !ctx.Navigation.IsNavigating)
            {
                ctx.Navigation.NavigateTo(gc, ArenaCenterPos);
            }

            Status = $"Ball Phase active ({(45 - (DateTime.Now - _phaseStartTime).TotalSeconds):F0}s remaining)";
            return BossEncounterResult.InProgress;
        }

        private BossEncounterResult TickWaitingForLoot(BotContext ctx, GameController gc, Vector2 playerGrid)
        {
            BotInput.ReleaseRightClick();
            var timeout = 1.0f; // 1s quick loot then exit
            var elapsed = (DateTime.Now - _phaseStartTime).TotalSeconds;

            if (elapsed >= timeout)
            {
                ctx.Log("[Exarch] 1s loot sweep finished — signaling Complete to exit");
                return BossEncounterResult.Complete;
            }

            var remaining = Math.Max(0, timeout - elapsed);
            var countdown = $"({remaining:F1}s left)";

            if (_bossDeathPos.HasValue)
            {
                var distToLoot = Vector2.Distance(playerGrid, _bossDeathPos.Value);
                if (distToLoot > 15 && !ctx.Navigation.IsNavigating)
                    ctx.Navigation.NavigateTo(gc, _bossDeathPos.Value);
            }

            if ((DateTime.Now - _lastLootScan).TotalMilliseconds >= 200)
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

            Status = $"Quick 1s loot {countdown}";
            return BossEncounterResult.InProgress;
        }

        private Entity? FindBoss(GameController gc)
        {
            try
            {
                foreach (var entity in gc.EntityListWrapper.ValidEntitiesByType[EntityType.Monster])
                {
                    if (entity.Rarity != MonsterRarity.Unique) continue;

                    if (entity.Path != null && entity.Path.Contains(BossPath, StringComparison.OrdinalIgnoreCase))
                        return entity;
                }
            }
            catch { }
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

            // Boss marker (Màu đỏ khi sống / chuẩn bị xuất hiện, Màu xanh khi đã chết)
            bool isBossDead = _phase == ExarchPhase.WaitingForLoot;
            if (_bossEntity != null)
            {
                var life = _bossEntity.GetComponent<Life>();
                if (_bossEntity.IsDead || (life != null && life.CurHP <= 0 && _bossWasAlive))
                {
                    isBossDead = true;
                }
            }

            if (isBossDead)
            {
                var deathPos = _bossDeathPos ?? (_bossEntity != null ? new Vector2(_bossEntity.GridPosNum.X, _bossEntity.GridPosNum.Y) : ArenaCenterPos);
                var world = Pathfinding.GridToWorld3D(gc, deathPos);
                var screen = cam.WorldToScreen(world);
                if (screen.X > -200 && screen.X < 2400)
                {
                    g.DrawText("THE SEARING EXARCH (ĐÃ CHẾT)", screen + new Vector2(-70, -30), SharpDX.Color.LimeGreen);
                }
            }
            else if (_bossEntity != null)
            {
                var life = _bossEntity.GetComponent<Life>();
                var hpText = life != null && life.MaxHP > 0 ? $" [{(float)life.CurHP / life.MaxHP * 100f:F0}%]" : "";
                var world = _bossEntity.BoundsCenterPosNum;
                var screen = cam.WorldToScreen(world);
                if (screen.X > -200 && screen.X < 2400)
                {
                    g.DrawText($"THE SEARING EXARCH{hpText}", screen + new Vector2(-50, -30), SharpDX.Color.Red);
                }
            }
            else if (_phase == ExarchPhase.NavigateToCenter || _phase == ExarchPhase.Fighting)
            {
                var world = Pathfinding.GridToWorld3D(gc, ArenaCenterPos);
                var screen = cam.WorldToScreen(world);
                if (screen.X > -200 && screen.X < 2400)
                {
                    g.DrawText("THE SEARING EXARCH (CHUẨN BỊ RA)", screen + new Vector2(-80, -30), SharpDX.Color.Red);
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
            BotInput.ReleaseRightClick();
            _phase = ExarchPhase.Idle;
            _bossEntity = null;
            _bossWasAlive = false;
            _hasEngagedBoss = false;
            _bossLastSeenAliveTime = DateTime.MinValue;
            _combatStartTime = DateTime.MinValue;
            _exploreFails = 0;
            _lastPlayerGrid = Vector2.Zero;
            _bossDeathPos = null;
            _lastLootScan = DateTime.MinValue;
            Status = "";
        }
    }
}
