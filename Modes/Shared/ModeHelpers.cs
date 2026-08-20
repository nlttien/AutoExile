using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using AutoExile.Systems;
using System.Numerics;

namespace AutoExile.Modes.Shared
{
    /// <summary>
    /// Static utilities shared across farming modes.
    /// </summary>
    public static class ModeHelpers
    {
        /// <summary>
        /// <summary>
        /// Find the best targetable portal entity (TownPortal, MapDevicePortal, AreaTransition, or custom MTX).
        /// </summary>
        public static Entity? FindNearestPortal(GameController gc)
        {
            if (gc?.EntityListWrapper?.OnlyValidEntities == null) return null;

            Entity? best = null;
            float bestDistSq = float.MaxValue;
            var playerPos = gc.Player?.GridPosNum ?? Vector2.Zero;

            foreach (var entity in gc.EntityListWrapper.OnlyValidEntities)
            {
                if (entity == null || !entity.IsValid || !entity.IsTargetable) continue;

                var path = entity.Path ?? string.Empty;
                var renderName = entity.RenderName ?? string.Empty;

                // Bắt buộc loại trừ chính cỗ máy Map Device để không click nhầm vào máy thay vì cổng portal
                if (renderName.Equals("Map Device", StringComparison.OrdinalIgnoreCase) ||
                    renderName.Contains("Mapping Device", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("/MappingDevice", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("/MapDevice", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("Metadata/MiscellaneousObjects/MapDevice", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("Metadata/Terrain/Hideout/Objects/MappingDevice", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isPortal = entity.Type == EntityType.TownPortal ||
                                entity.Type == EntityType.AreaTransition ||
                                path.Contains("MapDevicePortal", StringComparison.OrdinalIgnoreCase) ||
                                path.Contains("Town_Portals", StringComparison.OrdinalIgnoreCase) ||
                                path.Contains("SekhemaPortal", StringComparison.OrdinalIgnoreCase) ||
                                path.Contains("Portal", StringComparison.OrdinalIgnoreCase) ||
                                renderName.Contains("Portal", StringComparison.OrdinalIgnoreCase) ||
                                renderName.Contains("Absence", StringComparison.OrdinalIgnoreCase) ||
                                renderName.Contains("Crucible", StringComparison.OrdinalIgnoreCase) ||
                                renderName.Contains("Exarch", StringComparison.OrdinalIgnoreCase) ||
                                renderName.Contains("Eater", StringComparison.OrdinalIgnoreCase) ||
                                renderName.Contains("Maven", StringComparison.OrdinalIgnoreCase);

                if (!isPortal) continue;

                var distSq = Vector2.DistanceSquared(playerPos, entity.GridPosNum);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = entity;
                }
            }
            return best;
        }

        /// <summary>
        /// WorldToScreen → window offset → BotInput.Click. Updates lastActionTime on success.
        /// </summary>
        public static bool ClickEntity(GameController gc, Entity entity, ref DateTime lastActionTime)
        {
            if (!BotInput.CanAct) return false;
            if (!BotInput.ClickEntity(gc, entity)) return false;
            lastActionTime = DateTime.Now;
            return true;
        }

        /// <summary>
        /// BotInput gate + cooldown check.
        /// </summary>
        public static bool CanAct(DateTime lastActionTime, float cooldownMs)
        {
            return BotInput.CanAct &&
                   (DateTime.Now - lastActionTime).TotalMilliseconds >= cooldownMs;
        }

        /// <summary>
        /// Parse DefaultPositioning setting and enable combat with that profile.
        /// </summary>
        public static void EnableDefaultCombat(BotContext ctx)
        {
            var positioning = Enum.TryParse<CombatPositioning>(ctx.Settings.Build.DefaultPositioning.Value, out var pos)
                ? pos : CombatPositioning.Aggressive;
            ctx.Combat.SetProfile(new CombatProfile
            {
                Enabled = true,
                Positioning = positioning,
            });
        }

        /// <summary>
        /// Wrapper for StashSystem.HasInventoryItems.
        /// </summary>
        public static bool HasInventoryItems(GameController gc) => StashSystem.HasInventoryItems(gc);

        /// <summary>
        /// Cancel MapDevice + Stash + Interaction systems + release held keys.
        /// Called on area change and mode transitions.
        /// </summary>
        public static void CancelAllSystems(BotContext ctx)
        {
            var gc = ctx.Game;
            ctx.MapDevice.Cancel(gc, ctx.Navigation);
            if (ctx.Stash.IsBusy)
                ctx.Stash.Cancel(gc, ctx.Navigation);
            ctx.Interaction.Cancel(gc);
            BotInput.StopMovement();
            BotInput.ReleaseAllKeys();
        }
    }
}
