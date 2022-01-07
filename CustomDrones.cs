using Newtonsoft.Json;
using Oxide.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Custom Drones", "Nikedemos", "0.7.5")]
    [Description("Provides the base for handling extended Drone functionality")]
    public class CustomDrones : RustPlugin
    {
        #region CONST/STATIC
        public const string PREFAB_DRONE_DEPLOYED = "assets/prefabs/deployable/drone/drone.deployed.prefab";
        public const string PREFAB_MANDATORY_PREFIX = "assets/custom_drones/";
        public static Item.Flag FLAG_WAS_JUST_DEPLOYED = global::Item.Flag.Cooking;

        public const int HEADER_SIZE_BYTES = 80;

        #endregion
        #region TOP LEVEL DECLARATIONS
        public static bool Loading = true;
        public static bool Unloading = false;
        public static CustomDrones Instance;
        public static PreProcessedManager PreProcessedServer;

        public MetaData StoredMetaData;
        public PermaData StoredPermaData;

        public static GameObject SimulationGO;
        public static SimulationUpdater SimulationMono;

        #endregion
        #region HOOK METHODS
        private void Init()
        {
            Instance = this;
            lang.RegisterMessages(LangMessages, this);

            Loading = true;
            Unloading = false;

        }
        private void OnServerInitialized()
        {
            Instance = this;
            lang.RegisterMessages(LangMessages, this);

            SimulationUpdater.AllCustomDrones = new ListHashSet<DroneCustomBasic>();

            PreProcessedServer = new PreProcessedManager();


            Loading = false;
            Unloading = false;

            LoadMetaData();
            LoadPermaData();

            OnServerInitializedIntegrityCheck();
        }

        private void OnEntityKill(DroneCustomBasic drone)
        {
            if (IsObjectNull(Instance))
            {
                return;
            }

            drone.OnEntityKill();
        }

        object OnItemRemove(Item item)
        {
            if (IsObjectNull(Instance))
            {
                return null;
            }

            //ignore freshly built
            if (item.HasFlag(FLAG_WAS_JUST_DEPLOYED))
            {
                return null;
            }

            if (!PreProcessedServer.SkinIDToPrefabName.ContainsKey(item.skin))
            {
                return null;
            }

            if (IsObjectNull(item.instanceData))
            {
                return null;
            }

            if (!StoredPermaData.DroneData.ContainsKey(item.instanceData.dataInt))
            {
               return null;
            }

            StoredPermaData.DroneData.Remove(item.instanceData.dataInt);
            StoredPermaData.Dirty = true;

            return null;
        }

        void OnEntityBuilt(Planner planner, GameObject gObject)
        {
            if (!PreProcessedServer.SkinIDToPrefabName.ContainsKey(planner.skinID))
            {
                return;
            }

            var ownerItem = planner.GetItem();

            if (IsObjectNull(ownerItem))
            {
                return;
            }

            //IMPORTANT: immediately mark the item with an arbitrary flag, so it doesn't trigger
            //data removal!

            ownerItem.SetFlag(FLAG_WAS_JUST_DEPLOYED, true);

            Drone vanillaDroneToReplace = gObject.GetComponent<Drone>();

            if (IsObjectNull(vanillaDroneToReplace))
            {
                return;
            }

            var ownerID = vanillaDroneToReplace.OwnerID;
            var pos = vanillaDroneToReplace.transform.position;
            var rot = vanillaDroneToReplace.transform.eulerAngles;
            var skinID = vanillaDroneToReplace.skinID;

            vanillaDroneToReplace.limitNetworking = true;
            vanillaDroneToReplace.OnNetworkSubscribersLeave(Network.Net.sv.connections);

            vanillaDroneToReplace.Invoke(() => vanillaDroneToReplace.Kill(), 0.1F);

            int dataInt = -1;

            if (!IsObjectNull(ownerItem.instanceData))
            {
                dataInt = ownerItem.instanceData.dataInt;
            }

            DroneCustomBasic baseEntity = null;

            if (Instance.StoredPermaData.DroneData.ContainsKey(dataInt))
            {
                //check for collision!
                //there might be a case where you have just deployed a drone
                //and there already exists a drone with that data entry id.
                //in this case...

                bool collisionDetected = false;

                for (var i = 0; i < SimulationUpdater.AllCustomDrones.Count; i++)
                {
                    if (SimulationUpdater.AllCustomDrones[i].DroneDataBuffer.EntryID == dataInt)
                    {
                        collisionDetected = true;
                        break;
                    }
                }

                baseEntity = SummonWithData(dataInt, pos, rot, skinID, collisionDetected);
            }
            else
            {
                baseEntity = GameManager.server.CreateEntity(PreProcessedServer.SkinIDToPrefabName[skinID], pos, Quaternion.Euler(rot)) as DroneCustomBasic;
                if (!baseEntity)
                {
                    Debug.LogWarning("Couldn't create prefab:" + PreProcessedServer.SkinIDToPrefabName[skinID]);
                    return;
                }
            }

            baseEntity.Spawn();

        }

        private void Unload()
        {
            Unloading = true;
            StoredMetaData.Dirty = true;
            StoredPermaData.Dirty = true;

            OnServerSave();

            PreProcessedServer.Cleanup();

            if (!IsObjectNull(SimulationGO))
            {
                UnityEngine.Object.DestroyImmediate(SimulationGO);
            }

            foreach (var drone in BaseNetworkable.serverEntities.OfType<DroneCustomBasic>().ToArray())
            {
                drone.Kill();
            }

            SimulationUpdater.AllCustomDrones = null;

            Unloading = false;
            Instance = null;
        }

        private void OnServerSave()
        {
            if (IsObjectNull(Instance)) return;

            if (StoredPermaData.Dirty)
            {
                SavePermaData();
                StoredPermaData.Dirty = false;
            }

            if (StoredMetaData.Dirty)
            {
                SaveMetaData();
                StoredMetaData.Dirty = false;
            }
        }
        #endregion
        #region LANG
        public const string MSG_ANALYSING_INTEGRITY = nameof(MSG_ANALYSING_INTEGRITY);
        public const string MSG_FOUND_REFERENCED_PLUGIN = nameof(MSG_FOUND_REFERENCED_PLUGIN);
        public const string MSG_NOT_FOUND_REFERENCED_PLUGIN = nameof(MSG_NOT_FOUND_REFERENCED_PLUGIN);
        public const string MSG_PLUGINS_MISSING_ATTEMPTING_RELOAD = nameof(MSG_PLUGINS_MISSING_ATTEMPTING_RELOAD);
        public const string MSG_PLUGINS_MISSING_RELOAD_FAILURE = nameof(MSG_PLUGINS_MISSING_RELOAD_FAILURE);
        public const string MSG_PLUGINS_MISSING_RELOAD_SUCCESS = nameof(MSG_PLUGINS_MISSING_RELOAD_SUCCESS);

        public const string MSG_METADATA_LOADING = nameof(MSG_METADATA_LOADING);
        public const string MSG_METADATA_CORRUPT = nameof(MSG_METADATA_CORRUPT);
        public const string MSG_METADATA_NULL = nameof(MSG_METADATA_NULL);
        public const string MSG_METADATA_SAVING = nameof(MSG_METADATA_SAVING);

        public const string MSG_PERMADATA_LOADING = nameof(MSG_PERMADATA_LOADING);
        public const string MSG_PERMADATA_CORRUPT = nameof(MSG_PERMADATA_CORRUPT);
        public const string MSG_PERMADATA_NULL = nameof(MSG_PERMADATA_NULL);
        public const string MSG_PERMADATA_SAVING = nameof(MSG_PERMADATA_SAVING);

        public const string MSG_PREFAB_ALREADY_EXISTS = nameof(MSG_PREFAB_ALREADY_EXISTS);
        public const string MSG_DRONE_PREFAB_REGISTERED_SUCCESFULLY = nameof(MSG_DRONE_PREFAB_REGISTERED_SUCCESFULLY);
        public const string MSG_PREFAB_NAME_INCORRECT = nameof(MSG_PREFAB_NAME_INCORRECT);
        public const string MSG_SKINID_ALREADY_EXISTS = nameof(MSG_SKINID_ALREADY_EXISTS);
        public const string MSG_ERROR_DESERIALIZING_PREFAB_NOT_FOUND = nameof(MSG_ERROR_DESERIALIZING_PREFAB_NOT_FOUND);
        public const string MSG_ERROR_DESERIALIZING_ENTITY_NULL = nameof(MSG_ERROR_DESERIALIZING_ENTITY_NULL);
        public const string MSG_PLACEHOLDER_17 = nameof(MSG_PLACEHOLDER_17);
        public const string MSG_PLACEHOLDER_18 = nameof(MSG_PLACEHOLDER_18);
        public const string MSG_PLACEHOLDER_19 = nameof(MSG_PLACEHOLDER_19);
        public const string MSG_PLACEHOLDER_20 = nameof(MSG_PLACEHOLDER_20);

        public Dictionary<string, string> LangMessages = new Dictionary<string, string>
        {
            [MSG_METADATA_LOADING] = "Loading meta data...",
            [MSG_METADATA_CORRUPT] = "Corrupt meta data, generating default.",
            [MSG_METADATA_NULL] = "Null meta data, generating default.",
            [MSG_METADATA_SAVING] = "Saving meta data...",

            [MSG_PERMADATA_LOADING] = "Loading perma data...",
            [MSG_PERMADATA_CORRUPT] = "Corrupt perma data, generating default.",
            [MSG_PERMADATA_NULL] = "Null perma data, generating default.",
            [MSG_PERMADATA_SAVING] = "Saving perma data...",

            [MSG_ANALYSING_INTEGRITY] = "Analysing plugin bundle integrity...",
            [MSG_FOUND_REFERENCED_PLUGIN] = "Found referenced plugin: {0} by {1}",
            [MSG_NOT_FOUND_REFERENCED_PLUGIN] = "ERROR: Missing plugin [{0}]",
            [MSG_PLUGINS_MISSING_ATTEMPTING_RELOAD] = "It looks like you have some plugins missing. An attempt to manually reload them will be performed now...",
            [MSG_PLUGINS_MISSING_RELOAD_FAILURE] = "ERROR: Some of the previously seen sub-plugins are missing. Check your console for details. Please resolve the issue by placing those plugins back in your plugin directory or typing the command \"uninstall [PluginName]\" for all the missing entries if you no longer want to use them. After that, type \"Oxide.Reload CustomDrones\".",
            [MSG_PLUGINS_MISSING_RELOAD_SUCCESS] = "Successfully reloaded missing plugins. Proceeding...",

            [MSG_PREFAB_ALREADY_EXISTS] = "ERROR: Trying to register prefab {0}, but it already exists!",
            [MSG_DRONE_PREFAB_REGISTERED_SUCCESFULLY] = "Registered {0} as a custom type {1}",
            [MSG_PREFAB_NAME_INCORRECT] = "WARNING: Trying to register prefab {0}, but the format of the prefab path provided is not valid. The prefab is going to be registered as {1}",
            [MSG_SKINID_ALREADY_EXISTS] = "ERROR: Trying to register skin ID {0}, but it already exists!",
            [MSG_ERROR_DESERIALIZING_PREFAB_NOT_FOUND] = "ERROR: Could not deserialize the drone data. No prefab entry for {0}",
            [MSG_ERROR_DESERIALIZING_ENTITY_NULL] = "ERROR: Could not deserialize the drone data. Resulting entity is null.",
            [MSG_PLACEHOLDER_17] = "PLACEHOLDER 17",
            [MSG_PLACEHOLDER_18] = "PLACEHOLDER 18",
            [MSG_PLACEHOLDER_19] = "PLACEHOLDER 19",
            [MSG_PLACEHOLDER_20] = "PLACEHOLDER 20",

        };

        public static string MSG(string msg, string userID = null, params object[] args)
        {
            if (IsObjectNull(args))
            {
                return Instance.lang.GetMessage(msg, Instance, userID);
            }
            else
            {
                return string.Format(Instance.lang.GetMessage(msg, Instance, userID), args);
            }

        }

        #endregion
        #region PRE-PROCESSENING
        public class PreProcessedManager
        {
            private string[] _gameManifestEntitiesOriginal;
            private GameObject _droneEntitylessPrefab;
            private uint _dronePrefabID;
            private GameObjectRef _droneIdPanelPrefab;
            private GameObjectRef _droneImpactEffect;
            private float _droneLeanWeight;
            private Bounds _droneBounds;
            private AnimationCurve _droneMovLoopPitchCurve;
            private SoundDefinition _droneMovLoopSoundDef;
            private SoundDefinition _droneMovLoopStartSoundDef;
            private SoundDefinition _droneMovLoopStopSoundDef;

            private BaseCombatEntity.Pickup _dronePickup;

            public Hash<ulong, string> SkinIDToPrefabName = new Hash<ulong, string>();
            public Hash<string, ulong> PrefabNameToSkinID = new Hash<string, ulong>();

            private ProtectionProperties _customDroneProtectionProperties;

            //private DirectionProperties[] _dronePrefabDirectionProperties;

            private Hash<string, GameObject> _dronePrefabs = new Hash<string, GameObject>();

            //private PrefabAttribute.AttributeCollection _dronePrefabAttributeCollection;

            private float[] _droneProtectionAmounts = new float[25]
            {
                    1F, //Generic
                    1F, //Hunger
                    1F, //Thirst
                    1F, //Cold
                    1F, //Drowned
                    0.5F, //Heat
                    1F, //Bleeding
                    1F, //Poison
                    1F, //Suicide
                    0.1F, //Bullet //original is 0.99F
                    0.1F, //Slash //original is 0.99F
                    0.1F, //Blunt //original is 0.99F
                    0F, //Fall
                    1F, //Radiation
                    1F, //Bite
                    0.1F, //Stab
                    0.1F, //Explosion
                    1F, //RadiationExposure
                    1F, //ColdExposure
                    1F, //Decay
                    1F, //ElectricShock
                    0.5F, //Arrow
                    1F, //AntiVehicle
                    1F, //Collision
                    1F //Fun Water
            };

            public PreProcessedManager()
            {
                var originalPrefab = GameManager.server.FindPrefab(PREFAB_DRONE_DEPLOYED);
                _droneEntitylessPrefab = Facepunch.Instantiate.GameObject(originalPrefab);
                Drone originalEntity = _droneEntitylessPrefab.GetComponent<Drone>();

                _dronePrefabID = originalEntity.prefabID;
                _droneIdPanelPrefab = originalEntity.IDPanelPrefab;
                _droneImpactEffect = originalEntity.impactEffect;
                _droneLeanWeight = originalEntity.leanWeight;
                _droneBounds = originalEntity.bounds;
                _dronePickup = originalEntity.pickup;

                _droneEntitylessPrefab.name = originalPrefab.name;

                _droneMovLoopPitchCurve = originalEntity.movementLoopPitchCurve;
                _droneMovLoopSoundDef = originalEntity.movementLoopSoundDef;
                _droneMovLoopStartSoundDef = originalEntity.movementStartSoundDef;
                _droneMovLoopStopSoundDef = originalEntity.movementStopSoundDef;

                //_dronePrefabDirectionProperties = PrefabAttribute.server.FindAll<DirectionProperties>(_dronePrefabID);

                //_dronePrefabAttributeCollection = PrefabAttribute.server.prefabs[_dronePrefabID];

                UnityEngine.Object.DestroyImmediate(originalEntity);

                _customDroneProtectionProperties = ScriptableObject.CreateInstance<ProtectionProperties>();
                _customDroneProtectionProperties.comments = "DroneBotProtection";
                _customDroneProtectionProperties.amounts = _droneProtectionAmounts;

                _gameManifestEntitiesOriginal = GameManifest.Current.entities;

                
            }

            public string SanitizedPrefabName(string fullCustomPrefabName)
            {
                if (!fullCustomPrefabName.StartsWith(PREFAB_MANDATORY_PREFIX))
                {

                    if (fullCustomPrefabName.StartsWith("/"))
                    {
                        fullCustomPrefabName = fullCustomPrefabName.Substring(1);
                    }

                    fullCustomPrefabName = $"{PREFAB_MANDATORY_PREFIX}{fullCustomPrefabName}";
                }

                fullCustomPrefabName = Regex.Replace(fullCustomPrefabName, "[^0-9|a-zA-Z|.|-|_|\\/]", "");                               

                return fullCustomPrefabName.ToLower();
            }

            public GameObject RegisterDronePrefab<T>(string fullCustomPrefabName, ulong associatedSkinID) where T: DroneCustomBasic
            {
                var sanitized = SanitizedPrefabName(fullCustomPrefabName);

                if (fullCustomPrefabName != sanitized)
                {
                    Instance.PrintWarning(MSG(MSG_PREFAB_NAME_INCORRECT, null, fullCustomPrefabName, sanitized));
                    fullCustomPrefabName = sanitized;
                }

                if (IsRegistered(fullCustomPrefabName))
                {
                    Instance.PrintError(MSG(MSG_PREFAB_ALREADY_EXISTS, null, fullCustomPrefabName));
                    return null;
                }

                if (SkinIDToPrefabName.ContainsKey(associatedSkinID))
                {
                    Instance.PrintError(MSG(MSG_SKINID_ALREADY_EXISTS, null, associatedSkinID));
                    return null;
                }

                var newGameObject = Facepunch.Instantiate.GameObject(_droneEntitylessPrefab);
                newGameObject.name = _droneEntitylessPrefab.name;

                var modifiedDrone = newGameObject.AddComponent<T>();

                modifiedDrone.body = newGameObject.GetComponent<Rigidbody>();
                modifiedDrone.currentInput = new Drone.DroneInputState();

                //keep the original prefab ID intact
                modifiedDrone.prefabID = _dronePrefabID;

                modifiedDrone.pickup = _dronePickup;

                modifiedDrone.IDPanelPrefab = _droneIdPanelPrefab;
                modifiedDrone.impactEffect = _droneImpactEffect;
                modifiedDrone.leanWeight = _droneLeanWeight;

                var viewEyesGameObject = new GameObject("EyeTransform");
                viewEyesGameObject.SetActive(true);
                viewEyesGameObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                viewEyesGameObject.transform.SetParent(modifiedDrone.transform, false);
                viewEyesGameObject.transform.localPosition = new Vector3(0, 0.1F, 0.3F);
                viewEyesGameObject.transform.localEulerAngles = Vector3.zero;
                viewEyesGameObject.transform.forward = Vector3.forward;
                viewEyesGameObject.transform.up = Vector3.up;
                viewEyesGameObject.transform.hasChanged = true;

                modifiedDrone.viewEyes = viewEyesGameObject.transform;

                modifiedDrone.startHealth = 100F;
                modifiedDrone._maxHealth = 100F;
                modifiedDrone._health = 100F;
                modifiedDrone.sendsHitNotification = true;
                modifiedDrone.markAttackerHostile = true;
                modifiedDrone.syncPosition = true;
                modifiedDrone.baseProtection = _customDroneProtectionProperties;
                modifiedDrone.bounds = _droneBounds;//new Bounds(Vector3.zero, new Vector3(0.5F, 0.1F, 0.5F));
                modifiedDrone.movementLoopPitchCurve = _droneMovLoopPitchCurve;
                modifiedDrone.movementLoopSoundDef = _droneMovLoopSoundDef;
                modifiedDrone.movementStartSoundDef = _droneMovLoopStartSoundDef;
                modifiedDrone.movementStopSoundDef = _droneMovLoopStopSoundDef;

                modifiedDrone.triggers = new List<TriggerBase>();

                modifiedDrone.baseProtection = _customDroneProtectionProperties;

                var fakePrefabID = fullCustomPrefabName.ManifestHash();
                StringPool.Add(fullCustomPrefabName);

                SkinIDToPrefabName.Add(associatedSkinID, fullCustomPrefabName);
                PrefabNameToSkinID.Add(fullCustomPrefabName, associatedSkinID);

                modifiedDrone.skinID = associatedSkinID;

                //get the current manifest...

                var tempList = Facepunch.Pool.GetList<string>();

                for (var i = 0; i < GameManifest.Current.entities.Length; i++)
                {
                    tempList.Add(GameManifest.Current.entities[i]);
                }

                //and add yourself
                tempList.Add(fullCustomPrefabName);

                //and finalize
                GameManifest.Current.entities = tempList.ToArray();

                Facepunch.Pool.FreeList(ref tempList);

                _dronePrefabs.Add(fullCustomPrefabName, newGameObject);
                GameManager.server.preProcessed.AddPrefab(fullCustomPrefabName, newGameObject);

                //PrefabAttribute.server.prefabs.Add(fakePrefabID, _dronePrefabAttributeCollection);

                modifiedDrone.enableSaving = false;
                modifiedDrone.FakePrefabID = fakePrefabID;

                Instance.PrintWarning(MSG(MSG_DRONE_PREFAB_REGISTERED_SUCCESFULLY, null, modifiedDrone.PrefabName, modifiedDrone.GetType()));

                //ready to spawn now
                return newGameObject;
            }

            public bool UnregisterDronePrefab(string fullCustomPrefabName)
            {
                fullCustomPrefabName = SanitizedPrefabName(fullCustomPrefabName);

                if (!IsRegistered(fullCustomPrefabName))
                {
                    return false;
                }

                if (!PrefabNameToSkinID.ContainsKey(fullCustomPrefabName))
                {
                    return false;
                }

                ulong associatedSkinID = PrefabNameToSkinID[fullCustomPrefabName];

                var fakePrefabID = fullCustomPrefabName.ManifestHash();

                StringPool.toNumber.Remove(fullCustomPrefabName);
                StringPool.toString.Remove(fakePrefabID);

                SkinIDToPrefabName.Remove(associatedSkinID);
                PrefabNameToSkinID.Remove(fullCustomPrefabName);

                PrefabAttribute.server.prefabs.Remove(fakePrefabID);

                _dronePrefabs.Remove(fullCustomPrefabName);
                GameManager.server.preProcessed.Invalidate(fullCustomPrefabName);

                return true;

            }

            public bool IsRegistered(string fullCustomPrefabName)
            {
                return !IsObjectNull(GameManager.server.FindPrefab(fullCustomPrefabName));
            }

            public void Cleanup()
            {
                UnityEngine.Object.DestroyImmediate(_droneEntitylessPrefab);
                UnityEngine.Object.DestroyImmediate(_customDroneProtectionProperties);
                GameManifest.Current.entities = _gameManifestEntitiesOriginal;
            }
        }
        #endregion
        #region SIMULATION
        public class SimulationUpdater : MonoBehaviour
        {
            public static ListHashSet<DroneCustomBasic> AllCustomDrones;
            public bool Playing = true;

            void FixedUpdate()
            {
                if (!Playing)
                {
                    return;
                }

                for (var d = 0; d < AllCustomDrones.Count; d++)
                {
                    AllCustomDrones[d].OnFixedUpdate();
                }
            }

        }
        #endregion
        #region CUSTOM DRONE MONO CLASS

        public class DroneCustomBasic : Drone
        {
            public Vector3 SpawnPosition;
            public Vector3 SpawnRotation;

            public bool BrainUpdatesEnabled = true;
            public bool BodyUpdatesEnabled = true;

            public float LastBrainUpdate = float.MinValue;
            public float BrainUpdateRateBasic = 1F;
            public float BrainUpdateRateStagger = 0.5F;

            public float BrainUpdateRateCurrent;

            public DroneBuffer DroneDataBuffer = null;

            public string CorpsePrefab = null;

            public uint FakePrefabID;

            private bool _wasJustPickedUp = false;

            public void SummonedFromData(DroneBuffer dataEntry)
            {
                DroneDataBuffer = dataEntry;

                using (var stream = new MemoryStream(DroneDataBuffer.RawData))
                {
                    using (var reader = new BinaryReader(stream))
                    {
                        stream.Position = HEADER_SIZE_BYTES; 
                        OnLoadExtra(stream, reader);
                    }
                }
            }

            public override void ServerInit()
            {
                base.ServerInit();

                SpawnPosition = transform.position;
                SpawnRotation = transform.eulerAngles;

                RandomizeBrainUpdateRate();

                SimulationUpdater.AllCustomDrones.Add(this);

                bool needsNewID = false;

                if (IsObjectNull(DroneDataBuffer))
                {
                    //need new entry
                    DroneDataBuffer = new DroneBuffer
                    {
                        EntryID = Instance.StoredPermaData.AssignID(),
                        RawData = new byte[256]
                    };

                    needsNewID = true;

                    Instance.StoredPermaData.DroneData.Add(DroneDataBuffer.EntryID, DroneDataBuffer);
                }

                DroneDataBuffer.IsItem = false;

                if (needsNewID)
                {
                    UpdateIdentifier(OnNewRCIdentifierAssign());
                }

                string fakePrefabName = StringPool.Get(FakePrefabID);

                skinID = PreProcessedServer.PrefabNameToSkinID[fakePrefabName];

                SetPrivateFieldValue(this, "_prefabName", PreProcessedServer.SkinIDToPrefabName[skinID]);

                //disable native updates for performance
                enabled = false;

                DoServerInit();
            }

            public virtual void DoServerInit()
            {

            }

            public virtual string OnNewRCIdentifierAssign()
            {
                return RandomRemoteControlIdentifier();
            }

            public override void OnPickedUpPreItemMove(Item createdItem, BasePlayer player)
            {
                _wasJustPickedUp = true;

                DroneDataBuffer.IsItem = true;

                base.OnPickedUpPreItemMove(createdItem, player);
                               

                if (IsObjectNull(createdItem.instanceData))
                {
                    createdItem.instanceData = new ProtoBuf.Item.InstanceData();
                    createdItem.instanceData.ShouldPool = false;
                }

                createdItem.instanceData.dataInt = DroneDataBuffer.EntryID;

                SerializeDataToBuffer();
            }

            private void RandomizeBrainUpdateRate()
            {
                BrainUpdateRateCurrent = BrainUpdateRateBasic + UnityEngine.Random.Range(0F, BrainUpdateRateStagger);
            }

            public override float MaxVelocity()
            {
                //this helps hits register with AntiHack
                return float.MaxValue;
            }

            //disable native updates

            public override void Update()
            {
                return;
            }

            public new void FixedUpdate()
            {
                return;
            }

            public void OnEntityKill()
            {
                DoOnEntityKill();

                if (Unloading)
                {
                    return;
                }

                if (!_wasJustPickedUp)
                {
                    RemoveData();
                }

                if (IsObjectNull(SimulationUpdater.AllCustomDrones))
                {
                    return;
                }

                if (!SimulationUpdater.AllCustomDrones.Contains(this))
                {
                    return;
                }

                SimulationUpdater.AllCustomDrones.Remove(this);
            }

            public virtual void DoOnEntityKill()
            {

            }

            //and instead respond to these
            public void OnFixedUpdate()
            {
                OnBrainUpdate();

                if (!ShouldBodyUpdate())
                {
                    return;
                }

                DoBodyUpdate();
            }

            private void OnBrainUpdate()
            {
                if (!ShouldBrainUpdate())
                {
                    return;
                }

                if (Time.time < LastBrainUpdate + BrainUpdateRateCurrent)
                {
                    return;
                }

                DoBrainUpdate();

                LastBrainUpdate = Time.time;

                RandomizeBrainUpdateRate();
            }

            public virtual void DoBodyUpdate()
            {

            }
            public virtual void DoBrainUpdate()
            {

            }

            public virtual bool ShouldBrainUpdate()
            {
                return BrainUpdatesEnabled;
            }

            public virtual bool ShouldBodyUpdate()
            {
                return BodyUpdatesEnabled;
            }


            public void SerializeDataToBuffer()
            {
                using (var stream = new MemoryStream(256))
                {
                    using (var writer = new BinaryWriter(stream))
                    {
                        stream.Position = 0;
                        writer.Write(FakePrefabID); //4
                        writer.Write(OwnerID); //16
                        writer.Write(healthFraction); //4
                        writer.Write(transform.position.x); //4
                        writer.Write(transform.position.y); //4
                        writer.Write(transform.position.z); //4
                        writer.Write(transform.eulerAngles.x); //4
                        writer.Write(transform.eulerAngles.y); //4
                        writer.Write(transform.eulerAngles.z); //4

                        writer.Write(rcIdentifier.Substring(0, Mathf.Min(32, rcIdentifier.Length)));

                        //jumping to pos 80 will effectively pad with 0s if the rcIdentifier is less than 32 characters
                        stream.Position = HEADER_SIZE_BYTES;

                        OnSaveExtra(stream, writer);

                    }
                    DroneDataBuffer.RawData = stream.ToArray();
                    Instance.StoredPermaData.Dirty = true;
                }
            }

            public virtual void OnSaveExtra(MemoryStream stream, BinaryWriter writer)
            {
                //override this
            }

            public virtual void OnLoadExtra(MemoryStream stream, BinaryReader reader)
            {
                //and this
            }

            private void RemoveData()
            {
                var id = DroneDataBuffer.EntryID;

                Instance.StoredPermaData.DroneData.Remove(id);
                Instance.StoredPermaData.Dirty = true;
            }


        }
        #endregion
        #region INIT INTEGRITY CHECK
        public void OnServerInitializedIntegrityCheck()
        {
            Instance.NextTick(() =>
            {
                if (IsObjectNull(Instance))
                {
                    return;
                }

                //check if every plugin from PermaMetaRegistry has been loaded in.
                //if not
                var pluginsFound = Facepunch.Pool.GetList<string>();
                var pluginsNotFound = Facepunch.Pool.GetList<string>();

                foreach (var plugin in Interface.Oxide.RootPluginManager.GetPlugins())
                {
                    pluginsFound.Add(plugin.Name);
                }

            Instance.PrintWarning(MSG(MSG_ANALYSING_INTEGRITY));

                foreach (var plugin in StoredMetaData.PluginsSeen)
                {
                    if (pluginsFound.Contains(plugin.Key))
                    {
                        Instance.PrintWarning(MSG(MSG_FOUND_REFERENCED_PLUGIN, null, plugin.Key, plugin.Value.Author));
                        continue;
                    }

                    if (pluginsNotFound.Contains(plugin.Key))
                    {
                        continue;
                    }

                    Instance.PrintError(MSG(MSG_NOT_FOUND_REFERENCED_PLUGIN, null, plugin.Key));

                    pluginsNotFound.Add(plugin.Key);
                }

                bool success = true;

                bool clearMissingAfterwards = true;

                if (pluginsNotFound.Count > 0)
                {
                    clearMissingAfterwards = true;

                    Instance.PrintError(MSG(MSG_PLUGINS_MISSING_ATTEMPTING_RELOAD));

                    //first: check if you already have some missing plugins from previous load.
                    //if the plugin that's currently missing is mentioned in MissingPlugins,
                    //that means an attempt to reload it was ordered and has now failed.
                    //but if everything is okay, clear MissingPlugins.


                    //do we have any MissingPlugins? If so, it means we're already in the Reload Mode.
                    if (StoredMetaData.PluginsMissing.Count > 0)
                    {
                        foreach (var missing in pluginsNotFound)
                        {
                            if (StoredMetaData.PluginsMissing.Contains(missing))
                            {
                                clearMissingAfterwards = true;
                                success = false;
                                break;
                            }
                        }

                        if (!success)
                        {
                            Instance.PrintError(MSG(MSG_PLUGINS_MISSING_RELOAD_FAILURE));
                            IntegrityCheckFailure();
                        }
                        else
                        {
                            Instance.PrintWarning(MSG(MSG_PLUGINS_MISSING_RELOAD_SUCCESS));

                        }

                        clearMissingAfterwards = true;

                    }
                    else
                    {
                        clearMissingAfterwards = false;

                        foreach (var entry in pluginsNotFound)
                        {
                            StoredMetaData.PluginsMissing.Add(entry);
                        }
                        //Server.Command("Oxide.Load", pluginsNotFound.ToArray());
                        Server.Command("Oxide.Reload", pluginsNotFound.ToArray());
                        success = false;
                    }
                }

                if (success)
                {
                    IntegrityCheckSuccess();
                }

                if (clearMissingAfterwards)
                {
                    StoredMetaData.PluginsMissing.Clear();
                    StoredMetaData.Dirty = true;
                    SaveMetaData();
                }

                Facepunch.Pool.FreeList(ref pluginsFound);
                Facepunch.Pool.FreeList(ref pluginsNotFound);

            });
        }
        private void IntegrityCheckSuccess()
        {
            foreach (var plugin in StoredMetaData.PluginsSeen)
            {
                plugin.Value.Plugin.IntegrityCheckSuccess();
            }

            OnServerInitializedEverythingInOrder();
        }

        private void IntegrityCheckFailure()
        {
            foreach (var plugin in StoredMetaData.PluginsSeen)
            {
                plugin.Value.Plugin?.IntegrityCheckFailure();
            }
        }

        public void OnServerInitializedEverythingInOrder()
        {
            //continue loading the rest of the plugin

            SimulationGO = GetEmptyGameObject("DroneSimulation", Vector3.zero, Quaternion.identity);
            SimulationMono = SimulationGO.AddComponent<SimulationUpdater>();

            DroneCustomBasic drone;

            foreach (var entry in StoredPermaData.DroneData)
            {
                if (entry.Value.IsItem)
                {
                    continue;
                }

                drone = SummonWithData(entry.Value);

                if (IsObjectNull(drone))
                {
                    continue;
                }

                drone.Spawn();
            }
        }
        #endregion
        #region MISC HELPERS
        public static bool IsObjectNull(object obj) => ReferenceEquals(obj, null);

        public static void SetPrivateFieldValue<T>(object obj, string propName, T val)
        {
            if (obj == null) throw new ArgumentNullException("obj");
            Type t = obj.GetType();
            System.Reflection.FieldInfo fi = null;
            while (fi == null && t != null)
            {
                fi = t.GetField(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                t = t.BaseType;
            }
            if (fi == null) throw new ArgumentOutOfRangeException("propName", string.Format("Field {0} was not found in Type {1}", propName, obj.GetType().FullName));
            fi.SetValue(obj, val);
        }

        public static string RandomRemoteControlIdentifier(int length = 4)
        {
            StringBuilder builder = new StringBuilder(length);
            string result;
            do
            {
                builder.Clear();

                for (var i = 0; i< length; i++)
                {
                    builder.Append((char)(65 + UnityEngine.Random.Range(0, 26)));
                }

                result = builder.ToString();
            }
            while (RemoteControlEntity.IDInUse(result));

            return result;
        }

        public static DroneCustomBasic SummonWithData(int entryID, Vector3 overridePosition = default(Vector3), Vector3 overrideRotation = default(Vector3), ulong overrideOwnerID = default(ulong), bool makeCopyWithNewID = false)
        {
            if (!Instance.StoredPermaData.DroneData.ContainsKey(entryID))
            {
                return null;
            }

            return SummonWithData(Instance.StoredPermaData.DroneData[entryID], overridePosition, overrideRotation, overrideOwnerID, makeCopyWithNewID);
        }

        public static DroneCustomBasic SummonWithData(DroneBuffer workingEntry, Vector3 overridePosition = default(Vector3), Vector3 overrideRotation = default(Vector3), ulong overrideOwnerID = default(ulong), bool makeCopyWithNewID = false)
        {
            var deserialized = new DroneDataBasic();

            using (var stream = new MemoryStream(workingEntry.RawData))
            {
                using (var reader = new BinaryReader(stream))
                {
                    deserialized.FakePrefabID = reader.ReadUInt32(); //4
                    deserialized.OwnerID = reader.ReadUInt64(); //16
                    deserialized.HealthFraction = reader.ReadSingle(); //4
                    deserialized.Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); //12
                    deserialized.Rotation = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); //12

                    char currentChar;
                    long savePosition = stream.Position;

                    int foundLength = 0;

                    while (stream.Position < HEADER_SIZE_BYTES)
                    {
                        currentChar = reader.ReadChar();
                        if (currentChar == 0)
                        {
                            break;
                        }

                        foundLength++;
                    }

                    //back...
                    stream.Position = savePosition;
                    deserialized.RemoteID = new string(reader.ReadChars(foundLength));
                }
            }

            if (overridePosition != default(Vector3))
            {
                deserialized.Position = overridePosition;
            }

            if (overrideRotation != default(Vector3))
            {
                deserialized.Rotation = overrideRotation;
            }

            if (overrideOwnerID != default(ulong))
            {
                deserialized.OwnerID = overrideOwnerID;
            }

            if (makeCopyWithNewID)
            {
                workingEntry = workingEntry.CopyWithNewID();
                Instance.StoredPermaData.DroneData.Add(workingEntry.EntryID, workingEntry);
                Instance.StoredPermaData.Dirty = true;
            }

            var prefabName = StringPool.Get(deserialized.FakePrefabID);

            if (prefabName == string.Empty)
            {
                Instance.PrintWarning(MSG(MSG_ERROR_DESERIALIZING_PREFAB_NOT_FOUND, null, deserialized.FakePrefabID));
                return null;
            }

            var newDrone = GameManager.server.CreateEntity(prefabName, deserialized.Position, Quaternion.Euler(deserialized.Rotation)) as DroneCustomBasic;

            if (IsObjectNull(newDrone))
            {
                Instance.PrintWarning(MSG(MSG_ERROR_DESERIALIZING_ENTITY_NULL));
                return null;
            }

            newDrone.OwnerID = deserialized.OwnerID;
            newDrone.SetHealth(deserialized.HealthFraction * newDrone._maxHealth);
            newDrone.rcIdentifier = deserialized.RemoteID;

            newDrone.SummonedFromData(workingEntry);
            //ready to spawn now

            return newDrone;
        }

        public static GameObject GetEmptyGameObject(string name, Vector3 position, Quaternion rotation = default(Quaternion))
        {
            if (rotation == default(Quaternion))
            {
                rotation = Quaternion.identity;
            }

            var newGo = new GameObject(name);
            newGo.transform.SetPositionAndRotation(position, rotation);

            newGo.layer = (int)Rust.Layer.Reserved1;
            newGo.SetActive(true);

            return newGo;
        }

        #endregion
        #region PERMA DATA

        public struct DroneDataBasic
        {
            public uint FakePrefabID;
            public ulong OwnerID;
            public float HealthFraction;
            public Vector3 Position;
            public Vector3 Rotation;
            public string RemoteID;
        }

        public class DroneBuffer
        {
            public int EntryID;
            public byte[] RawData;
            public bool IsItem = false;

            public DroneBuffer CopyWithNewID(int newID = default(int))
            {
                if (newID == default(int))
                {
                    newID = Instance.StoredPermaData.AssignID();
                }

                return new DroneBuffer
                {
                    EntryID = newID,
                    RawData = RawData.ToArray(),
                    IsItem = IsItem
                };
            }
        }

        public class PermaData
        {
            public Hash<int, DroneBuffer> DroneData = new Hash<int, DroneBuffer>();

            public int LastDataID = 0;

            public int AssignID()
            {
                LastDataID++;
                Dirty = true;
                return LastDataID - 1;
            }

            [JsonIgnore]
            public bool Dirty = true;
        }
        public void LoadPermaData()
        {
            PrintWarning(MSG(MSG_PERMADATA_LOADING));

            try
            {
                StoredPermaData = Interface.Oxide.DataFileSystem.ReadObject<PermaData>(Name + ".PERMADATA");
            }
            catch
            {
                PrintWarning(MSG(MSG_PERMADATA_CORRUPT));
                NewPermaData();
            }

            if (IsObjectNull(StoredPermaData))
            {
                PrintWarning(MSG(MSG_PERMADATA_NULL));
                NewPermaData();
            }
        }
        public void SavePermaData()
        {
            PrintWarning(MSG(MSG_PERMADATA_SAVING));

            for (var i = 0; i<SimulationUpdater.AllCustomDrones.Count; i++)
            {
                SimulationUpdater.AllCustomDrones[i].SerializeDataToBuffer();
            }

            Interface.Oxide.DataFileSystem.WriteObject(Name + ".PERMADATA", StoredPermaData);
        }
        public void NewPermaData()
        {
            StoredPermaData = new PermaData();
            SavePermaData();
        }
        #endregion
        #region META DATA
        public class SubPluginEntry
        {
            public string Name = "Plugin Name";
            public string Author = "Plugin Author";
            public VersionNumber Version;

            [JsonIgnore]
            public CustomDronesPlugin Plugin;
        }

        public class MetaData
        {
            public Dictionary<string, SubPluginEntry> PluginsSeen = new Dictionary<string, SubPluginEntry>();
            public List<string> PluginsMissing = new List<string>();
            [JsonIgnore]
            public bool Dirty = true;
        }
        public void LoadMetaData()
        {
            PrintWarning(MSG(MSG_METADATA_LOADING));

            try
            {
                StoredMetaData = Interface.Oxide.DataFileSystem.ReadObject<MetaData>(Name + ".METADATA");
            }
            catch
            {
                PrintWarning(MSG(MSG_METADATA_CORRUPT));
                NewMetaData();
            }

            if (IsObjectNull(StoredMetaData))
            {
                PrintWarning(MSG(MSG_METADATA_NULL));
                NewMetaData();
            }
        }
        public void SaveMetaData()
        {
            PrintWarning(MSG(MSG_METADATA_SAVING));

            Interface.Oxide.DataFileSystem.WriteObject(Name + ".METADATA", StoredMetaData);
        }
        public void NewMetaData()
        {
            StoredMetaData = new MetaData();
            SaveMetaData();
        }
        #endregion
        #region EXTEND THIS SUB-PLUGIN CLASS
        public class CustomDronesPlugin : RustPlugin
        {
            public static CustomDrones CustomDronesInstance;
            #region HOOKS
            void Init()
            {
                OnSubPluginInit();
            }

            void OnServerInitialized()
            {

                OnSubPluginServerInitialized();
            }
            void Unload()
            {
                OnSubPluginUnload();

            }

            public virtual void IntegrityCheckSuccess()
            {
                OnPluginPreProcessedRegistration();
            }

            public virtual void IntegrityCheckFailure()
            {

            }

            public virtual void OnPluginPreProcessedRegistration()
            {
                //this is where you register
            }

            public virtual void OnPluginPreProcessedUnregistration()
            {
                //this is where you unregister
            }

            public virtual void OnSubPluginInit()
            {
                CustomDronesInstance = Instance;
            }

            public virtual void OnSubPluginServerInitialized()
            {
                CustomDronesInstance = Instance;

                if (!CustomDronesInstance.StoredMetaData.PluginsSeen.ContainsKey(Name))
                {
                    CustomDronesInstance.StoredMetaData.PluginsSeen.Add(Name, new SubPluginEntry
                    {
                        Name = Name,
                        Version = Version,
                        Author = Author,
                    });

                    CustomDronesInstance.StoredMetaData.Dirty = true;
                }
                else
                {
                    if (CustomDronesInstance.StoredMetaData.PluginsSeen[Name].Version != Version)
                    {
                        OnVersionMigrationStuff(CustomDronesInstance.StoredMetaData.PluginsSeen[Name].Version, Version);

                        CustomDronesInstance.StoredMetaData.PluginsSeen[Name].Version = Version;


                        CustomDronesInstance.StoredMetaData.Dirty = true;

                    }
                }
                CustomDronesInstance.StoredMetaData.PluginsSeen[Name].Plugin = this;
            }
            public virtual void OnSubPluginUnload()
            {
                OnPluginPreProcessedUnregistration();
                CustomDronesInstance = null;
            }

            public virtual void OnVersionMigrationStuff(VersionNumber oldVersion, VersionNumber newVersion)
            {

            }
            #endregion
        }

        #endregion
    }
}

