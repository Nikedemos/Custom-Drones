// Requires: CustomDrones

using Oxide.Core;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Oxide.Plugins.CustomDrones;

namespace Oxide.Plugins
{
    [Info("CustomDronesSubExample", "Nikedemos", "1.0.0")]
    [Description("This plugin contains everything you need to register, spawn and use your custom Drone types")]
    public class CustomDronesSubExample : CustomDronesPlugin
    {
        //all custom prefab names...
        //- must be lowercase
        //- must start with assets/custom_drones/
        //- must NOT contain any characters apart from letters, numbers, dashes, periods, underscores, forward slashes.

        public const string PREFAB_EXAMPLE = PREFAB_MANDATORY_PREFIX + "nikedemos/drone.example.prefab";
        private static CustomDronesSubExample SubInstance;

        public class DroneCustomExample : DroneCustomBasic
        {
            //these are just for a lazy movement on a unit circle with sines and cosines
            private bool _goingForward = true;
            private float _progress = 0F;

            private Vector3 _posOffsetLocal;

            private static float PROGRESS_WRAP = 4F * Mathf.PI;

            public override void ServerInit()
            {
                base.ServerInit();

                _posOffsetLocal = Vector3.zero;

                //turn off gravity and all the extra stuff
                body.isKinematic = true;
                body.useGravity = false;

                enableGrounding = false;
                altitudeAcceleration = 0F;
                keepAboveTerrain = false;

                SubInstance.PrintWarning($"My name is {PrefabName}, but everybody calls me {ShortPrefabName}. My unique prefab ID is {prefabID} and I'm of type {GetType()}\nI'm going to fly around in a circle, changing direction on every brain update, and then kill myself after 10 seconds.");

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
            }
        }

        #region REGISTRATION
        public override void IntegrityCheckSuccess()
        {
            base.IntegrityCheckSuccess();

            SubInstance.PrintWarning($"\n{Name} is ready! Spawning a test drone at 0,0,0...\n");

            var testDrone = GameManager.server.CreateEntity(PREFAB_EXAMPLE, Vector3.zero, Quaternion.identity);
            testDrone.Spawn();
        }

        public override void OnPluginPreProcessedRegistration()
        {
            PreProcessedServer.RegisterDronePrefab<DroneCustomExample>(PREFAB_EXAMPLE);
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
