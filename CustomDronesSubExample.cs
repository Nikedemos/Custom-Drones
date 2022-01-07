// Requires: CustomDrones

using System.IO;
using System.Linq;
using UnityEngine;
using static Oxide.Plugins.CustomDrones;

namespace Oxide.Plugins
{
    [Info("CustomDronesSubExample", "Nikedemos", "1.0.1")]
    [Description("This plugin contains everything you need to register, spawn and use your custom Drone types")]
    public class CustomDronesSubExample : CustomDronesPlugin
    {
        //all custom prefab names...
        //- must be lowercase
        //- must start with assets/custom_drones/
        //- must NOT contain any characters apart from letters, numbers, dashes, periods, underscores, forward slashes.

        //of course you don't have to define the constants at all, it's just to make things easier
        public const string PREFAB_SHORTNAME = "drone.example";
        public const string PREFAB_EXAMPLE = PREFAB_MANDATORY_PREFIX + "nikedemos/" + PREFAB_SHORTNAME +".prefab";

        public const ulong SKINID_EXAMPLE = 2436737889;

        private static CustomDronesSubExample SubInstance;

        public class DroneCustomExample : DroneCustomBasic
        {
            //these are just for a lazy movement on a unit circle with sines and cosines
            private bool _goingForward = true;
            private float _progress = 0F;

            private Vector3 _posOffsetLocal;

            private static float PROGRESS_WRAP = 4F * Mathf.PI;

            public override void OnSaveExtra(MemoryStream stream, BinaryWriter writer)
            {
                writer.Write(_goingForward);
                writer.Write(_progress);
            }

            public override void OnLoadExtra(MemoryStream stream, BinaryReader reader)
            {
                _goingForward = reader.ReadBoolean();
                _progress = reader.ReadSingle();
            }

            public override void DoServerInit()
            {
                _posOffsetLocal = Vector3.zero;

                //turn off gravity and all the extra stuff
                body.isKinematic = true;
                body.useGravity = false;

                enableGrounding = false;
                altitudeAcceleration = 0F;
                keepAboveTerrain = false;

                SubInstance.PrintWarning($"My name is {PrefabName}, my skin is {skinID} but everybody calls me {ShortPrefabName}. My unique prefab ID is {prefabID} and I'm of type {GetType()}\nI'm going to fly around in a circle, changing direction on every brain update.\nMy data id is {DroneDataBuffer.EntryID}!\nAnd my RC ID is {rcIdentifier}");
            }

            public override void DoBrainUpdate()
            {
                //switch direction
                _goingForward = !_goingForward;
                SubInstance.PrintWarning(transform.position.ToString());
            }

            public override void DoBodyUpdate()
            {
                //update body. this is executed on every single frame, so keep the expensive stuff in DoBrainUpdate
                _progress += Time.deltaTime * (_goingForward ? 1F : -1F);

                if (_progress > PROGRESS_WRAP)
                {
                    _progress -= PROGRESS_WRAP;
                }
                else if (_progress < -PROGRESS_WRAP)
                {
                    _progress += PROGRESS_WRAP;
                }

                _posOffsetLocal = new Vector3(Mathf.Sin(_progress), 0F, Mathf.Cos(_progress));

                transform.position = SpawnPosition + _posOffsetLocal;
                transform.eulerAngles = new Vector3(transform.eulerAngles.x, (_progress / PROGRESS_WRAP) * 360F, transform.eulerAngles.z);
                transform.hasChanged = true;

                var firstPlayer = BasePlayer.activePlayerList.FirstOrDefault();

                firstPlayer?.SendConsoleCommand("ddraw.text", Time.fixedDeltaTime, Color.red, transform.position, DroneDataBuffer.EntryID);
            }
        }

        #region REGISTRATION
        public override void IntegrityCheckSuccess()
        {
            base.IntegrityCheckSuccess();

            //SubInstance.PrintWarning($"\n{Name} is ready! Spawning a test drone at 0,0,0...\n");

            //var testDrone = GameManager.server.CreateEntity(PREFAB_EXAMPLE, Vector3.zero, Quaternion.identity);
            //testDrone.Spawn();
        }

        public override void OnPluginPreProcessedRegistration()
        {
            PreProcessedServer.RegisterDronePrefab<DroneCustomExample>(PREFAB_EXAMPLE, SKINID_EXAMPLE);
        }

        public override void OnPluginPreProcessedUnregistration()
        {
            PreProcessedServer.UnregisterDronePrefab(PREFAB_EXAMPLE);
        }
        #endregion

        #region LEAVE THESE ALONE UNLESS YOU KNOW WHAT YOU'RE DOING

        void Init()
        {
            OnSubPluginInit();
            SubInstance = null;
        }

        void Unload()
        {
            OnSubPluginUnload();
            SubInstance = null;

        }

        void OnServerInitialized()
        {
            OnSubPluginServerInitialized();
            SubInstance = this;
        }
        #endregion
    }
}
