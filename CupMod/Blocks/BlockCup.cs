using CupMod.Entities;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;


namespace CupMod.Blocks
{
    public class BlockCup : BlockLiquidContainerTopOpened
    {
        private bool IsCurrentlyThrowing = false;
        private bool IsThrowingEnabled = true;
        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
    {
        WorldInteraction[] baseInteractions = base.GetHeldInteractionHelp(inSlot, ref handling);

        if (inSlot?.Itemstack != null && GetCurrentLitres(inSlot.Itemstack) <= 0)
        {
            WorldInteraction[] interactions = new WorldInteraction[baseInteractions.Length + 1];

            interactions[0] = new WorldInteraction()
            {
                ActionLangCode = "daymarescupmod:blockhelp-throwcup",
                MouseButton = EnumMouseButton.Right
            };

            for (int i = 0; i < baseInteractions.Length; i++)
            {
                interactions[i + 1] = baseInteractions[i];
            }

            return interactions;
        }

        return baseInteractions;
    }

        public override void OnHeldInteractStart(ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            IsThrowingEnabled = api.World.Config.GetBool("IsThrowingEnabled", true);
            if (IsThrowingEnabled == false)
            {
                base.OnHeldInteractStart(itemslot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
                IsCurrentlyThrowing = false;
                return;
            }
            if (GetCurrentLitres(itemslot.Itemstack) > 0 || byEntity.Controls.ShiftKey)
            {
                base.OnHeldInteractStart(itemslot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
                IsCurrentlyThrowing = false;
                return;
            }
            if (GetCurrentLitres(itemslot.Itemstack) == 0 && !byEntity.Controls.ShiftKey)
            {
                if (blockSel == null)
                {
                    StartThrowing(byEntity, ref handHandling);
                    return;
                }
                var world = byEntity.World;
                var block = world.BlockAccessor.GetBlock(blockSel.Position, BlockLayersAccess.Fluid);
                if (block is BlockWater /* or check block.Code/path to water */)
                {
                    base.OnHeldInteractStart(itemslot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
                    IsCurrentlyThrowing = false;
                    return;
                }
                else
                {
                    StartThrowing(byEntity, ref handHandling);
                    return;
                }
                
            }
        }

        public void StartThrowing(EntityAgent byEntity, ref EnumHandHandling handHandling)
        {
            Console.WriteLine("[Cup Mod] Starting throwing...");
            IsCurrentlyThrowing = true;
            byEntity.Attributes.SetInt("aiming", 1);
            byEntity.Attributes.SetInt("aimingCancel", 0);
            byEntity.StartAnimation("aim");
            handHandling = EnumHandHandling.PreventDefault;
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            base.OnHeldInteractStep(secondsUsed, slot, byEntity, blockSel, entitySel);
            if (secondsUsed >= 0.95f && IsCurrentlyThrowing == false)
            {
                base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel);
            }

            if (IsCurrentlyThrowing && IsThrowingEnabled)
            {
                base.OnHeldInteractStep(secondsUsed, slot, byEntity, blockSel, entitySel);
                bool result = true;
                bool preventDefault = false;
                foreach (CollectibleBehavior behavior in CollectibleBehaviors)
                {
                    EnumHandling handled = EnumHandling.PassThrough;

                    bool behaviorResult = behavior.OnHeldInteractStep(secondsUsed, slot, byEntity, blockSel, entitySel, ref handled);
                    if (handled != EnumHandling.PassThrough)
                    {
                        result &= behaviorResult;
                        preventDefault = true;
                    }

                    if (handled == EnumHandling.PreventSubsequent) return result;
                }
                if (preventDefault) return result;

                if (byEntity.Attributes.GetInt("aimingCancel") == 1) return false;

                if (byEntity.World is IClientWorldAccessor)
                {
                    ModelTransform tf = new ModelTransform();
                    tf.EnsureDefaultValues();

                    float offset = GameMath.Clamp(secondsUsed * 3, 0, 1.5f);

                    tf.Translation.Set(offset / 4f, offset / 2f, 0);
                    tf.Rotation.Set(0, 0, GameMath.Min(90, secondsUsed * 360 / 1.5f));
                }
                return true;
            }
            return true;
            
        }

        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            base.OnHeldInteractCancel(secondsUsed, slot, byEntity, blockSel, entitySel, cancelReason);
            if (IsCurrentlyThrowing)
            {
                IsCurrentlyThrowing = false;
                byEntity.Attributes.SetInt("aiming", 0);
                byEntity.StopAnimation("aim");

                if (cancelReason != EnumItemUseCancelReason.ReleasedMouse)
                {
                    byEntity.Attributes.SetInt("aimingCancel", 1);
                }

                return true;
            }
            return true;
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel);
            if (IsCurrentlyThrowing && IsThrowingEnabled)
            { 
                IsCurrentlyThrowing = false;
                bool preventDefault = false;

                foreach (CollectibleBehavior behavior in CollectibleBehaviors)
                {
                    EnumHandling handled = EnumHandling.PassThrough;

                    behavior.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel, ref handled);
                    if (handled != EnumHandling.PassThrough) preventDefault = true;

                    if (handled == EnumHandling.PreventSubsequent) return;
                }

                if (preventDefault) return;

                if (byEntity.Attributes.GetInt("aimingCancel") == 1) return;

                byEntity.Attributes.SetInt("aiming", 0);
                byEntity.StopAnimation("aim");

                if (secondsUsed < 0.35f) return;
                if (byEntity.World.Side == EnumAppSide.Client) return;
                float damage = 1;
                ItemStack stack = slot.TakeOut(1);
                //Used for glass and clay types
                string cup_color = stack.Collectible.Variant.ContainsKey("color") ? stack.Collectible.Variant["color"] : "";
                //Used for tankards
                string cup_wood = stack.Collectible.Variant.ContainsKey("wood") ? stack.Collectible.Variant["wood"] : "";
                string cup_metal = stack.Collectible.Variant.ContainsKey("metal") ? stack.Collectible.Variant["metal"] : "";

                string cup_type = stack.Collectible.Code.FirstCodePart();
                Console.WriteLine(cup_type);
                slot.MarkDirty();

                IPlayer byPlayer = null;
                if (byEntity is EntityPlayer) byPlayer = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
                byEntity.World.PlaySoundAt(new AssetLocation("game:sounds/player/throw"), byEntity, byPlayer, false, 8);
               
                EntityProperties type;
                float breakChance = 0.0f;
                //Cup entity is created - uses base name of cup type (claycup, wineglass, etc) and color to create entity
                breakChance = api.World.Config.GetFloat(cup_type + "BreakChance");
                //Console.WriteLine("[Cup Mod] Loaded break chance for " + cup_type + " as " + breakChance.ToString());
                switch (cup_type)
                {
                    case "claycup":
                        type = byEntity.World.GetEntityType(new AssetLocation("daymarescupmod", $"throwncup-{cup_color}"));
                        break;
                    case "claymug":
                        type = byEntity.World.GetEntityType(new AssetLocation("daymarescupmod", $"thrownmug-{cup_color}"));
                        break;
                    case "clayshot":
                        type = byEntity.World.GetEntityType(new AssetLocation("daymarescupmod", $"thrownshot-{cup_color}"));
                        break;
                    case "wineglass":
                        type = byEntity.World.GetEntityType(new AssetLocation("daymarescupmod", $"thrownwineglass-{cup_color}"));
                        break;
                    case "tankard":
                        type = byEntity.World.GetEntityType(new AssetLocation("daymarescupmod", $"throwntankard-{cup_wood}-{cup_metal}"));
                        break;
                    default:
                        type = byEntity.World.GetEntityType(new AssetLocation("daymarescupmod", $"throwncup-{cup_color}"));
                        break;
                }
                if (type == null)
                {
                    byEntity.World.Logger.Warning("[Cup Mod] Could not find thrown entity type for {0}", stack.Collectible.Code);
                    slot.Itemstack = stack;
                    slot.MarkDirty();
                    return;
                }
                Entity entity = byEntity.World.ClassRegistry.CreateEntity(type);
                ((EntityThrownCup)entity).FiredBy = byEntity;
                ((EntityThrownCup)entity).Damage = damage;
                ((EntityThrownCup)entity).ProjectileStack = stack;
                ((EntityThrownCup)entity).HorizontalImpactBreakChance = breakChance;
                ((EntityThrownCup)entity).VerticalImpactBreakChance = breakChance/2.0f;

                float acc = 1 - byEntity.Attributes.GetFloat("aimingAccuracy", 0);
                double rndpitch = byEntity.WatchedAttributes.GetDouble("aimingRandPitch", 1) * acc * 0.75;
                double rndyaw = byEntity.WatchedAttributes.GetDouble("aimingRandYaw", 1) * acc * 0.75;

                Vec3d pos = byEntity.Pos.XYZ.Add(0, byEntity.LocalEyePos.Y, 0);
                Vec3d aheadPos = pos.AheadCopy(1, byEntity.Pos.Pitch + rndpitch, byEntity.Pos.Yaw + rndyaw);
                Vec3d velocity = (aheadPos - pos) * 0.5;

                entity.Pos.SetPosWithDimension(
                    byEntity.Pos.BehindCopy(0.21).XYZ.Add(0, byEntity.LocalEyePos.Y, 0)
                );

                entity.Pos.Motion.Set(velocity);

                entity.Pos.SetFrom(entity.Pos);
                entity.World = byEntity.World;

                byEntity.World.SpawnEntity(entity);
                byEntity.StartAnimation("throw");

                //byEntity.GetBehavior<EntityBehaviorHunger>()?.ConsumeSaturation(2f);
            }

        }
    }
}
