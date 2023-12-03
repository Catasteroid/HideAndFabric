using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using System.Text;
using System;
 

namespace HideAndFabric
{
    public class Core : ModSystem
    { 
        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            api.RegisterEntityBehaviorClass("wool", typeof(EntityBehaviorWool));
        }
    }

    public class EntityBehaviorWool : EntityBehavior
    {
        /// <summary>
        /// The attribute tree of this EntityBehaviorClass, used to save/load data for the specific entity
        /// </summary>
        ITreeAttribute shearTree;

        /// <summary>
        /// A double representing the base number of hours required for a single of unit of wool to be produced by the entity, used in the definition
        /// of the value HoursPerUnit where it is modified by the value of hoursPerWoolUnitGenerationalReduction up to a cap provided by hoursPerWoolUnitMaxReduction
        /// </summary>
        double hoursPerWoolUnit;

        /// <summary>
        /// A double representing the per-generation value reduction of hours needed to produce one wool unit, used in the definition of the value
        /// HoursPerUnit where either this value multiplied by the generation value of the entity or the maximum defined by hoursPerWoolUnitMaxReduction
        /// is substracted from hoursPerWoolUnit, whichever is smaller, to produce the resolved number of hours needed per wool unit
        /// </summary>
        double hoursPerWoolUnitGenerationalReduction;

        /// <summary>
        /// A double representing a cap on the maximum per-generation reduction value subtracted from hoursPerWoolUnit in defining the value of HoursPerUnit,
        /// either the value of hoursPerWoolUnitGenerationalReduction multiplied by the entity's generation value or this value is used, whichever is smaller
        /// </summary>
        double hoursPerWoolUnitMaxReduction;

        // Minimum wool quantity worth shearing, below which shearing won't happen
        /// <summary>
        /// An integer representing the minimum number of wool units required for the entity to be sheared, below which shearing is disallowed
        /// </summary>
        int minWool;

        /// <summary>
        /// An integer representing the base maximum wool unit amount the animal can grow before growth stops, this value is used by MaxWoolCount
        /// and has GenerationalWoolBonus multiplied by the entity's generation number added to it to resolve the actual maximum wool number
        /// </summary>
        // Maximum wool quantity that can be provided by shearing for a minGen generation animal
        int maxWool;
        /// <summary>
        /// An integer representing the bonus amount of product provided when shearing is performed with shears instead of a knife,
        /// this is to represent potential losses of product when using an inappropriate tool, can be 0 to make both tools produce 
        /// the same amount of product
        /// </summary>

        // The bonus item count provided when using shears instead of a knife
        int shearsBonus;
        /// <summary>
        /// The assetLocation of the object produced when shearing is performed, can be either an item or a block
        /// </summary>

        AssetLocation wool;
        /// <summary>
        /// The assetLocation of the sound played when shearing is performed
        /// </summary>

        AssetLocation shearSound;

        /// <summary>
        /// An integer representing the minimum generation value below which shearing is not possible, generational max wool growth and
        /// wool growth speed modifiers are modified by this value, for example max generational growth bonus of 10 for minGen 3 would be 
        /// modified to cap at 13
        /// </summary>
        // The minimum generation value required to shear the animal at all
        int minGen;

        /// <summary>
        /// An integer representing the cap above which the maximum wool growth modifier represented by generationalMaxWoolGrowthBonus
        /// no longer provides a bonus, the generation values are counted above minGen
        /// </summary>
        // The cap above which generation values above minGen stop providing bonuses from the generationalMaxWoolGrowthBonus multiplier GenerationalWoolBonus
        int maxGenBonus;

        /// <summary>
        /// The attribute tree of this EntityBehaviorClass, used to save/load data for the specific entity
        /// </summary>
        // Extra maximum wool growth provided per-generation value above minGen
        int generationalMaxWoolGrowthBonus;

        /// <summary>
        /// A double representing the percentage reduction in scratch chance modifier multiplied by the entity's generation value 
        /// </summary>
        // The reduction in scratch chance (in percentage, 0.05 == 5%), capped at 5%
        double generationalScratchChanceReduction;

        /// <summary>
        /// A double representing the base percentage chance of shearing to deal damage to the entity
        /// </summary>
        double scratchChance;

        /// <summary>
        /// A double representing the number of hours required to produce a single unit of wool,
        /// modified by the reduction modifier represented by the integer GenerationalWoolBonus
        /// and capped by the maximum generational reduction represented by generationalMaxWoolGrowthBonus
        /// </summary>
        internal double HoursPerUnit
        {
            get
            {
                double u = hoursPerWoolUnit - GameMath.Max((Generation - minGen) * hoursPerWoolUnitGenerationalReduction, hoursPerWoolUnitMaxReduction);
                return u;
            }
        }

        /// <summary>
        /// An integer representing the maximum amount of extra wool the creature can produce due to it's generation or the maximum wool bonus whichever is smaller
        /// </summary>
        internal int GenerationalWoolBonus
        {
            get
            {
                int g = GameMath.Min((Generation - minGen) * generationalMaxWoolGrowthBonus,maxGenBonus);
                return g;
            }
        }

        /// <summary>
        /// An integer holding a timestamp of whenever the last time shearing occurred
        /// </summary>
        internal double LastShearTime
        {
            get 
            {
                return shearTree.GetDouble("lastShear"); 
            }
            set 
            { 
                shearTree.SetDouble("lastShear", value);  
                entity.WatchedAttributes.MarkPathDirty("wool"); 
            }
        }

        /// <summary>
        /// Returns the number of hours of growth between the last shear time and now
        /// </summary>
        internal double Growth
        {
            get 
            { 
                return entity.World.Calendar.TotalHours - LastShearTime; 
            }
        }

        /// <summary>
        /// An integer representing the maximum amount of wool the animal can grow for it's generation number
        /// </summary>
        internal int MaxWoolCount
        {
            get
            {
                return maxWool + GenerationalWoolBonus;
            }
        }

        /// <summary>
        /// An integer returning the amount of wool the animal has grown since it's last shearing or MaxWoolCount, whichever is smallest
        /// </summary>
        internal int WoolCount
        {
            get
            {
                double u = Growth / HoursPerUnit;
                //double c = Growth / shearableAt * (maxWool + GenerationalWoolBonus);

                return u >= minWool ? (int)GameMath.Clamp(u, minWool,MaxWoolCount) : 0;
            } 
        }

        /// <summary>
        /// Returns the animal's current generation number
        /// </summary>
        internal int Generation
        {
            get 
            {
                return entity.WatchedAttributes.GetInt("generation");
            }
        }

        /// <summary>
        /// Returns an integer made up of WoolCount plus the bonus provided by using shears represented by shearsBonus or 0 if WoolCount is 0
        /// </summary>
        internal int WoolCountBonus
        {
            get 
            {
                return WoolCount > 0 ? WoolCount + shearsBonus : 0;
            }
        }

        /// <summary>
        /// A double representing a percentage chance of the animal being injured during shearing modified by generational reduction multiplied by the animal's generation
        /// </summary>
        internal double ModifiedScratchChance
        {
            get
            {
                double s = scratchChance;
                s = GameMath.Max(0.05,s - (generationalScratchChanceReduction * Generation));
                return s;
            }
        }

        /// <summary>
        /// Called when an existing entity with this behaviour is loaded from saved data, calls Init()
        /// </summary>
        public override void OnEntityLoaded()
        {
            init();
        }

        /// <summary>
        /// Called when an entity with this behaviour is spawned into the world, calls Init()
        /// </summary>
        public override void OnEntitySpawn()
        {
            init();
        }

        /// <summary>
        /// Per-entity initialisation method, if an existing entity with this behaviour was loaded in from data of a saved generated chunk
        /// then lastShearTime is set to the saved value, if a new entity with this behaviour is spawned in then it sets lastShearTime to the current time
        /// </summary>
        void init()
        {
            LastShearTime = entity.WatchedAttributes.GetDouble("lastShearTime");
            //entity.World.Logger.Error("Lastsheartime for {0} is {1}.", entity.EntityId, LastShearTime);
            if (entity.World.Side == EnumAppSide.Client) return;
            LastShearTime = Math.Min(LastShearTime, entity.World.Calendar.TotalHours);
        }

        /// <summary>
        /// General initilisation method, sets and saves all the behaviour's important attributes, using default values if the entity object's JSON
        /// file does not define those attributes
        /// </summary>
        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            hoursPerWoolUnit = attributes["hoursPerWoolUnit"].AsDouble(24.0);
            hoursPerWoolUnitGenerationalReduction = attributes["hoursPerWoolUnitGenerationalReduction"].AsDouble(0.5);
            hoursPerWoolUnitMaxReduction = attributes["hoursPerWoolUnitMaxReduction"].AsDouble(20.0);
            maxGenBonus = attributes["maxGenBonus"].AsInt(8);
            generationalMaxWoolGrowthBonus = attributes["generationalMaxWoolGrowthBonus"].AsInt(8);
            generationalScratchChanceReduction = attributes["generationalScratchChanceReduction"].AsDouble(0.05);
            //shearableAt = attributes["shearableAt"].AsInt(216);
            minWool = attributes["minQuantity"].AsInt(4);
            maxWool = attributes["maxQuantity"].AsInt(12);
            shearsBonus = attributes["shearsBonus"].AsInt(4);
            minGen = attributes["minGen"].AsInt(3);
            scratchChance = attributes["scratchChance"].AsDouble(0.5);
            wool = new AssetLocation(attributes["woolItem"].AsString("hideandfabric:woolfibers"));
            shearSound = new AssetLocation(attributes["shearSound"].AsString());

            if (shearTree == null)
            {
                entity.WatchedAttributes.SetAttribute("wool", shearTree = new TreeAttribute());
            }
        }

        /// <summary>
        /// Performs the scratch/injury roll against the modified scratch chance and if the roll succeeds then it deals damage to the sheared entity
        /// </summary>
        public void ReceiveShearDamage(EntityAgent byEntity, double multiplier)
        {
            double n = entity.World.Rand.NextDouble();
            if (n < GameMath.Max(0.05,ModifiedScratchChance * multiplier))
            {
                entity.ReceiveDamage(new DamageSource() { Source = EnumDamageSource.Entity, SourceEntity = byEntity, Type = EnumDamageType.SlashingAttack }, 1);
            }
        }

        /// <summary>
        /// Checks the sanity checks in CanShear and calls the shearing method in DoShear if they return true
        /// </summary>
        public bool TryShear(EntityAgent byEntity, ItemSlot itemslot)
        {
            if (CanShear(itemslot, out bool bonus))
            {
                DoShear(itemslot, byEntity, bonus);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Sanity checks to determine whether shearing can actually happen, calls in-game error messages if on the client side
        /// </summary>
        public bool CanShear(ItemSlot itemslot, out bool bonus)
        {
            bonus = false;
            // Don't show an error or even attempt shearing if the creature is dead
            if (!entity.Alive) return false;

            if (entity.Api.Side == EnumAppSide.Server && WoolCount > 0 && Generation >= minGen)
            {
                EnumTool tool = itemslot?.Itemstack?.Collectible.Tool ?? (EnumTool)(-1);
                switch (tool)
                {
                    case EnumTool.Knife:
                        return true;
                    case EnumTool.Shears:
                        bonus = true;
                        return true;
                    default:
                        break;
                }
            }

            if (entity.World.Api is ICoreClientAPI capi && entity.Api.Side == EnumAppSide.Client)
                {
                    if (Generation < minGen)
                    {
                        capi.TriggerIngameError(this, "nothighenoughgeneration", Lang.Get("animaltoowildshearclient",Generation));
                        return false;
                    }
                    else if (WoolCount == 0)
                    {
                        capi.TriggerIngameError(this, "notenoughwool", Lang.Get("animalinsufficientwoolclient", minWool));
                        return false;
                    }
                }
            return false;
        }

        /// <summary>
        /// The function where shearing actually happens, calculates appropriate wool amount, spawns the item stack, plays the sound, deals damage if necessary, sets the last shear time and reduces durability of the tool used 
        /// </summary>
        public void DoShear(ItemSlot slot, EntityAgent byEntity, bool bonused = false)
        {
            int woolCount = bonused ? WoolCountBonus : WoolCount;

            ItemStack woolStack = new ItemStack(entity.World.GetItem(wool), woolCount);
            if (!byEntity.TryGiveItemStack(woolStack))
                {
                    byEntity.World.SpawnItemEntity(woolStack, byEntity.Pos.XYZ.Add(0, 0.5, 0));
                }
            //entity.World.SpawnItemEntity(woolStack, entity.SidedPos.XYZ);
            ReceiveShearDamage(byEntity, 1.0);
            LastShearTime = entity.World.Calendar.TotalHours;
            byEntity.World.PlaySoundAt(shearSound, entity);
            byEntity.WatchedAttributes.SetDouble("lastShearTime", (double)LastShearTime);
            //byEntity.World.Logger.Error("Lastsheartime for {0} is {1}.", byEntity.EntityId, LastShearTime);
            slot.Itemstack.Item.DamageItem(byEntity.World, byEntity, slot);
            slot.MarkDirty();
        }

        /// <summary>
        /// Function called when the right mouse button is pressed or held while aiming at an animal with this behaviour, calls TryShear then calls the base method
        /// </summary>
        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
        {
            //System.Diagnostics.Debug.WriteLine(Growth);
            
            TryShear(byEntity, itemslot);

            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
        }
        
        /// <summary>
        /// The function handling the display of information in the animal's tooltip at the top of the interface, displays info about whether the animal is high enough generation to shear,
        /// how long until the animal can next be sheared, how much wool shearing would provide, the maximum amount of wool the animal can grow
        /// </summary>
        public override void GetInfoText(StringBuilder infotext)
        {
            if (!entity.Alive) {
                base.GetInfoText(infotext);
                return;
            } 
            if (Generation < minGen)
            {
                infotext.AppendLine(Lang.Get("hideandfabric:insufficientgen",(int)minGen));
                base.GetInfoText(infotext);
                return;
            }
            shearTree = entity.WatchedAttributes.GetTreeAttribute("wool");
            if (shearTree == null) return;

            if (WoolCount == 0){
                infotext.AppendLine(Lang.Get("hideandfabric:woolgrowing"));
                int hoursUntilMin = minWool * (int)HoursPerUnit;
                if (hoursUntilMin >= 25)
                {
                    infotext.AppendLine(Lang.Get("hideandfabric:daysuntilshearable",hoursUntilMin / 24));
                }
                else
                {
                    infotext.AppendLine(Lang.Get("hideandfabric:hoursuntilshearable",hoursUntilMin));
                }
            }
            else
            {
                infotext.AppendLine(Lang.Get("hideandfabric:shearable",WoolCount));
                infotext.AppendLine(Lang.Get("hideandfabric:shearsbonus",shearsBonus));
            }

            int hoursUntilMax = (int)((double)MaxWoolCount * HoursPerUnit);
            if (hoursUntilMax >= 25)
            {
                infotext.AppendLine(Lang.Get("hideandfabric:daysmaxwool",hoursUntilMax / 24));
            }
            else
            {
                infotext.AppendLine(Lang.Get("hideandfabric:hoursmaxwool",hoursUntilMax));
            }
            infotext.AppendLine(Lang.Get("hideandfabric:animalmaxwool",MaxWoolCount));
            base.GetInfoText(infotext);
        }
                   
        public EntityBehaviorWool(Entity entity) : base(entity)
        {
        }

        /// <summary>
        /// Returns a string that represents the name of this entity behaviour object
        /// </summary>
        public override string PropertyName()
        {
            return "wool";
        }
    }
}