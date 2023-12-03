using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.API;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;

using Vintagestory.GameContent;

namespace entityExtension
{

        public class Core : ModSystem
    { 
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
			api.RegisterEntityBehaviorClass("multiplywithevolution", typeof(EntityBehaviorMultiplyWithEvolution));
        }
    }


    public class EntityBehaviorMultiplyWithEvolution : EntityBehaviorMultiplyBase
    {
        JsonObject typeAttributes;
        long callbackId;

        /// <summary>
        /// Float to indicate the gestation period of the parent in days
        /// </summary>
        internal float PregnancyDays
        {
            get { return typeAttributes["pregnancyDays"].AsFloat(3f); }
        }

        /// <summary>
        /// Entity codes used if the generation number of the parent is below the evolution threshold 
        /// </summary>
        internal AssetLocation[] SpawnEntityCodes
        {
            get { return AssetLocation.toLocations(typeAttributes["spawnEntityCodes"].AsArray<string>(new string[0])); }
        }
        /// <summary>
        /// Entity codes used if the generation number of the parent exceeds the threshold 
        /// </summary>
        internal AssetLocation[] SpawnEvolvedEntityCodes
        {
            //get { return AssetLocation.toLocations(typeAttributes["spawnEvolvedEntityCodes"].AsStringArray(new string[0])); }
            get { return AssetLocation.toLocations(typeAttributes["spawnEvolvedEntityCodes"].AsArray<string>(new string[0])); }
        }
        /// <summary>
        /// Extra data to determine individual thresholds for each of the spawnEvolvedEntityCodes
        /// </summary>
        internal int[] EvolutionGenerationThresholds
        {
            get { return typeAttributes["evolutionGenerationThresholds"].AsArray<int>(new int[0]); }
        }

        internal string RequiresNearbyEntityCode
        {
            get { return typeAttributes["requiresNearbyEntityCode"].AsString(""); }
        }

        /// <summary>
        /// Array of one or more entity codes that are looked for in a volume check to find a relevant male entity for pregnancy to occur 
        /// </summary>
        internal string[] RequiresNearbyEntityCodes
        {
            get { return typeAttributes["requiresNearbyEntityCodes"].AsArray<string>(new string[0]); }
        }

        /// <summary>
        /// The range at which the volume search for (presumably male) entities listed in RequiresNearbyEntityCodes will look for those entities
        /// </summary>
        internal float RequiresNearbyEntityRange
        {
            get { return typeAttributes["requiresNearbyEntityRange"].AsFloat(5); }
        }

        /// <summary>
        /// If true only the evolved child entity code list is used to determine what entity the child is, otherwise if false the normal entity code list is appended to that array when the choice is made
        /// </summary>
        internal bool ExclusiveEvolution
        {
            get { return typeAttributes["exclusiveEvolution"].AsBool(true); }
        }

        /*internal int GrowthCapQuantity
        {
            get { return attributes["growthCapQuantity"].AsInt(10); }
        }
        internal float GrowthCapRange
        {
            get { return attributes["growthCapRange"].AsFloat(10); }
        }
        internal AssetLocation[] GrowthCapEntityCodes
        {
            get { return AssetLocation.toLocations(attributes["growthCapEntityCodes"].AsStringArray(new string[0])); }
        }*/

        public float SpawnQuantityMin
        {
            get { return typeAttributes["spawnQuantityMin"].AsFloat(1); }
        }
        public float SpawnQuantityMax
        {
            get { return typeAttributes["spawnQuantityMax"].AsFloat(2); }
        }


        public double TotalDaysLastBirth
        {
            get { return multiplyTree.GetDouble("totalDaysLastBirth", -9999); }
            set { multiplyTree.SetDouble("totalDaysLastBirth", value); }
        }

        public double TotalDaysPregnancyStart
        {
            get { return multiplyTree.GetDouble("totalDaysPregnancyStart"); }
            set { multiplyTree.SetDouble("totalDaysPregnancyStart", value); }
        }

        public bool IsPregnant
        {
            get { return multiplyTree.GetBool("isPregnant"); }
            set { multiplyTree.SetBool("isPregnant", value); }
        }

        bool eatAnyway = false;

        public override bool ShouldEat
        {
            get
            {
                return 
                    eatAnyway || 
                    (
                        !IsPregnant 
                        && GetSaturation() < PortionsEatenForMultiply 
                        && TotalDaysCooldownUntil <= entity.World.Calendar.TotalDays
                    )
                ;
            }
        }

        public EntityBehaviorMultiplyWithEvolution(Entity entity) : base(entity)
        {

        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            this.typeAttributes = attributes;

            if (entity.World.Side == EnumAppSide.Server)
            {
                if (!multiplyTree.HasAttribute("totalDaysLastBirth"))
                {
                    TotalDaysLastBirth = -9999;
                }

                callbackId = entity.World.RegisterCallback(CheckMultiplyEvolve, 3000);
            }
        }


        private void CheckMultiplyEvolve(float dt)
        {
            if (!entity.Alive) return;

            callbackId = entity.World.RegisterCallback(CheckMultiplyEvolve, 3000);

            if (entity.World.Calendar == null) return;

            double daysNow = entity.World.Calendar.TotalDays;

            if (!IsPregnant)
            {
                if (TryGetPregnant())
                {
                    IsPregnant = true;
                    TotalDaysPregnancyStart = daysNow;
                }

                return;
            }


            /*if (GrowthCapQuantity > 0 && IsGrowthCapped())
            {
                TimeLastMultiply = entity.World.Calendar.TotalHours;
                return;
            }*/

            
            if (daysNow - TotalDaysPregnancyStart > PregnancyDays)
            {
                Random rand = entity.World.Rand;

                float q = SpawnQuantityMin + (float)rand.NextDouble() * (SpawnQuantityMax - SpawnQuantityMin);
                TotalDaysLastBirth = daysNow;
                TotalDaysCooldownUntil = daysNow + (MultiplyCooldownDaysMin + rand.NextDouble() * (MultiplyCooldownDaysMax - MultiplyCooldownDaysMin));
                IsPregnant = false;
                entity.WatchedAttributes.MarkPathDirty("multiplywithevolution");

                // Gonna need this a bit earlier than in the basic multiply behavior
                int generation = entity.WatchedAttributes.GetInt("generation", 0);
                entity.World.Logger.Error("Entity with code '{0}' of generation {1} attempting to give birth, with {2} SpawnEntityCodes and {3} SpawnEvolvedEntityCodes", entity.Code,generation,SpawnEntityCodes.Length,SpawnEvolvedEntityCodes.Length);
                // Checks to see which configuration of generational treshold definitions is used 
                List<AssetLocation> childCodes = new List<AssetLocation>();
                // Per-type generation threshold values; 
                if (EvolutionGenerationThresholds.Length > 0 && EvolutionGenerationThresholds.Length == SpawnEvolvedEntityCodes.Length)
                {
                   
                    // Go through each type and check to see if the parent's generation is larger than or equal to the threshold value
                    for (var i = 0; i < SpawnEvolvedEntityCodes.Length; i++)
                    {
                        if (generation >= EvolutionGenerationThresholds[i]) 
                            childCodes.Add(SpawnEvolvedEntityCodes[i]);
                    }
                    // If the generation was too low for all evolved entity codes just use the normal entity codes
                    
                    if (childCodes.Count == 0)
                    {
                        childCodes.AddRange(SpawnEntityCodes);
                        entity.World.Logger.Error("Entity with code '{0}' of generation {1} had no valid SpawnEvolvedEntityCodes for that generation, using {2} SpawnEntityCodes instead.", entity.Code,generation,SpawnEntityCodes.Length);
                    }
                    // Or if exclusive evolution is false the normal entity codes are added to the evolved entity code list 
                    if (ExclusiveEvolution == false)
                    {
                        entity.World.Logger.Error("Entity with code '{0}' of generation {1} using non-exclusive evolution, with {2} valid SpawnEvolvedEntityCodes for that generation, adding {3} SpawnEntityCodes to the list.", entity.Code,generation,childCodes.Count,SpawnEntityCodes.Length);
                        childCodes.AddRange(SpawnEntityCodes);
                    }    

                }
                // Only one generation threshold value; use it for all alternate child entity codes
                else if (EvolutionGenerationThresholds.Length == 1 && generation >= EvolutionGenerationThresholds[0])
                {
                    childCodes.AddRange(SpawnEvolvedEntityCodes);
                    entity.World.Logger.Error("Entity with code '{0}' of generation {1} using single generation threshold, with {2} valid SpawnEvolvedEntityCodes for that generation.", entity.Code,generation,childCodes.Count);
                    if (ExclusiveEvolution == false)
                        entity.World.Logger.Error("Entity with code '{0}' of generation {1} using non-exclusive evolution, with {2} valid SpawnEvolvedEntityCodes for that generation, adding {3} SpawnEntityCodes to the list.", entity.Code,generation,childCodes.Count,SpawnEntityCodes.Length);
                        childCodes.AddRange(SpawnEntityCodes);
                }
                // Assume it's some wierd badly configured setup with mismatching threshold/code numbers
                else
                {
                    entity.World.Logger.Error("Misconfigured entity. Entity with code '{0}' is configured with {1} EvolutionGenerationThresholds but has {2} SpawnEvolvedEntityCodes, using {3} SpawnEntityCodes instead.", entity.Code,EvolutionGenerationThresholds.Length,SpawnEvolvedEntityCodes.Length,SpawnEntityCodes.Length);
                    childCodes.AddRange(SpawnEntityCodes);
                }
                
                if (SpawnEntityCodes.Length < 1 && SpawnEvolvedEntityCodes.Length >= 1)
                {
                    entity.World.Logger.Error("Misconfigured entity. Entity with code '{0}' is configured with no SpawnEntityCodes but has {1} SpawnEvolvedEntityCodes for some reason.", entity.Code,SpawnEvolvedEntityCodes.Length);
                    return;
                }

                AssetLocation[] childEntityCodes = childCodes.ToArray();

                // Return if both spawnEntityCodes and spawnEvolvedEntityCodes are blank
                // 
                if (childEntityCodes.Length == 0)
                {
                    entity.World.Logger.Error("Misconfigured entity. Entity with code '{0}' is configured (via MultiplyWithEvolve behavior) to give birth but could find no valid SpawnEntityCodes or SpawnEvolvedEntityCodes.", entity.Code);
                    return;
                }

                //EntityProperties childType = entity.World.GetEntityType(SpawnEntityCode);

                AssetLocation code = childEntityCodes[entity.World.Rand.Next(childEntityCodes.Length)];
                if (code == null)
                {
                    entity.World.Logger.Error("Misconfigured entity. Entity with code '{0}' is configured (via MultiplyWithEvolve behavior) to give birth to '{1}', but no such entity type was registered.", entity.Code, code);
                    return;
                }
                // Select one of multiple child types at random 
                EntityProperties childType = entity.World.GetEntityType(code);
                    
                while (q > 1 || rand.NextDouble() < q)
                {
                    q--;
                    Entity childEntity = entity.World.ClassRegistry.CreateEntity(childType);

                    childEntity.ServerPos.SetFrom(entity.ServerPos);
                    childEntity.ServerPos.Motion.X += (rand.NextDouble() - 0.5f) / 20f;
                    childEntity.ServerPos.Motion.Z += (rand.NextDouble() - 0.5f) / 20f;

                    childEntity.Pos.SetFrom(childEntity.ServerPos);
                    childEntity.Attributes.SetString("origin", "reproduction");
                    childEntity.WatchedAttributes.SetInt("generation", generation + 1);
                    entity.World.SpawnEntity(childEntity);
                }

            }

            entity.World.FrameProfiler.Mark("multiplywithevolution");
        }

        private bool TryGetPregnant()
        {
            if (entity.World.Rand.NextDouble() > 0.06) return false;
            if (TotalDaysCooldownUntil > entity.World.Calendar.TotalDays) return false;

            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (tree == null) return false;

            float saturation = tree.GetFloat("saExtensionturation", 0);
            
            if (saturation >= PortionsEatenForMultiply)
            {
                Entity maleentity = null;
                if (RequiresNearbyEntityCodes.Length > 0 && (maleentity = GetRequiredEntityNearby()) == null) return false;

                if (entity.World.Rand.NextDouble() < 0.2)
                {
                    tree.SetFloat("saturation", saturation - 1);
                    return false;
                }

                tree.SetFloat("saturation", saturation - PortionsEatenForMultiply);

                if (maleentity != null)
                {
                    ITreeAttribute maletree = maleentity.WatchedAttributes.GetTreeAttribute("hunger");
                    if (maletree != null)
                    {
                        saturation = maletree.GetFloat("saturation", 0);
                        maletree.SetFloat("saturation", Math.Max(0, saturation - 1));
                    }
                }

                IsPregnant = true;
                TotalDaysPregnancyStart = entity.World.Calendar.TotalDays;
                entity.WatchedAttributes.MarkPathDirty("multiplywithevolution");

                return true;
            }

            return false;
        }


        private Entity GetRequiredEntityNearby()
        {
            //if (RequiresNearbyEntityCode == null) return null;
            if (RequiresNearbyEntityCodes.Length == 0) return null;

            return entity.World.GetNearestEntity(entity.ServerPos.XYZ, RequiresNearbyEntityRange, RequiresNearbyEntityRange, (e) =>
            {
                foreach(string checkEntityString in RequiresNearbyEntityCodes)
                {
                    if (e.WildCardMatch(new AssetLocation(checkEntityString)))
                    {
                        if (!e.WatchedAttributes.GetBool("doesEat") || (e.WatchedAttributes["hunger"] as ITreeAttribute)?.GetFloat("saturation") >= 1)
                        {
                            return true;
                        }
                    }
                }

                return false;

            });
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            entity.World.UnregisterCallback(callbackId);
            base.OnEntityDespawn(despawn);
        }



        public override string PropertyName()
        {
            return "multiplywithevolution";
        }

        public override void GetInfoText(StringBuilder infotext)
        {
            multiplyTree = entity.WatchedAttributes.GetTreeAttribute("multiplywithevolution");

            if (IsPregnant) infotext.AppendLine(Lang.Get("Is pregnant"));
            else
            {
                if (entity.Alive)
                {
                    ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
                    if (tree != null)
                    {
                        float saturation = tree.GetFloat("saturation", 0);
                        infotext.AppendLine(Lang.Get("Portions eaten: {0}", saturation));
                    }

                    double daysLeft = TotalDaysCooldownUntil - entity.World.Calendar.TotalDays;

                    if (daysLeft > 0)
                    {
                        if (daysLeft > 3)
                        {
                            infotext.AppendLine(Lang.Get("Several days left before ready to mate"));
                        }
                        else
                        {
                            infotext.AppendLine(Lang.Get("Less than 3 days before ready to mate"));
                        }

                    }
                    else
                    {
                        infotext.AppendLine(Lang.Get("Ready to mate"));
                    }
                }
            }
        }
    }
}