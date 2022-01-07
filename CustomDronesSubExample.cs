// Requires: CustomDrones
//this is a hard dependency, so it requires a... "Requires".
using System.IO;
using System.Linq;
using UnityEngine;
using static Oxide.Plugins.CustomDrones;

namespace Oxide.Plugins
{
    [Info("CustomDronesSubExample", "Nikedemos", "1.0.2")]
    [Description("This plugin contains everything you need to register, spawn and use your custom Drone types")]

    //notice that this class does NOT extend RustPlugin directly - it extends the class from CustomDrones.
    //it's required to maintain the correct order and flow of multi-part plugin shenanigans.
    public class CustomDronesSubExample : CustomDronesPlugin
    {
        //all custom prefab names...
        //- must be lowercase
        //- must start with assets/custom_drones/
        //- must NOT contain any characters apart from letters, numbers, dashes, periods, underscores, forward slashes.

        //of course you don't have to define the constants at all, it's just to make things easier
        public const string PREFAB_SHORTNAME = "drone.example";
        public const string PREFAB_EXAMPLE = PREFAB_MANDATORY_PREFIX + "nikedemos/" + PREFAB_SHORTNAME +".prefab";

        //the skin must be unique (it doesn't have to actually exist on the Workshop, but if it doesn't, the icon will just keep trying and failing to download)
        //this is the skin that all prototypes, entities and pickup Items associated with your custom type will have
        public const ulong SKINID_EXAMPLE = 2436737889;

        //standard shit, the instance of this plugin class
        private static CustomDronesSubExample SubInstance;

        //your custom class must extend DroneCustomBasic
        //see the relevant class in CustomDrones.cs 
        public class DroneCustomExample : DroneCustomBasic
        {
            //these are just for a lazy movement on a unit circle with sines and cosines
            //this is just an example of some arbitrary data that can be stored
            //also, I like it when things move in circles, it's very therapeutic
            private bool _goingForward = true;
            private float _progress = 0F;

            private Vector3 _posOffsetLocal;

            private static float PROGRESS_WRAP = 4F * Mathf.PI;

            public override void OnSaveExtra(MemoryStream stream, BinaryWriter writer)
            {
                //the cursor of the stream is currently at 80, so just keep writing your stuff
                //it must be in the same order as in OnLoadExtra!
                writer.Write(_goingForward);
                writer.Write(_progress);
            }

            public override void OnLoadExtra(MemoryStream stream, BinaryReader reader)
            {
                //the cursor of the stream is currently at 80, so just keep writing your stuff
                //it must be in the same order as in OnSaveExtra!
                _goingForward = reader.ReadBoolean();
                _progress = reader.ReadSingle();
            }

            //override this for the OnServerInit logic specifics.
            //do not override the OnServerInit directly.
            public override void DoServerInit()
            {
                //it's already false by default.
                //if false, the Update, FixedUpdate and LateUpdate methods of a member of this class won't be called.
                enabled = false;

                //you can set this to false if you want to implement InvokeRepeating or something like that.
                //if false, the DoBrainUpdate() will not be called when the simulation updates.
                BrainUpdatesEnabled = true;

                //body update is just the equivalent of FixedUpdate. Set to false and DoBodyUpdate() will not be called when the simulation updates.
                BodyUpdatesEnabled = true;

                //minimum time between brain updates
                BrainUpdateRateBasic = 1F;

                //randomly added time so spread the updates in time a bit
                BrainUpdateRateStagger = 0.1F; 

                //these values below are completely optional and depend purely on your implementation
                _posOffsetLocal = Vector3.zero;

                //turn off gravity and all the extra stuff sine we're not using rigidbody physics/forces

                body.isKinematic = true;
                body.useGravity = false;

                enableGrounding = false;
                altitudeAcceleration = 0F;
                keepAboveTerrain = false;

                //display some debug values to make sure it works
                SubInstance.PrintWarning($"My name is {PrefabName}, my skin is {skinID} but everybody calls me {ShortPrefabName}. My unique prefab ID is {prefabID} and I'm of type {GetType()}\nI'm going to fly around in a circle, changing direction on every brain update.\nMy data id is {DroneDataBuffer.EntryID}!\nAnd my RC ID is {rcIdentifier}");
            }

            //won't be called if BrainUpdatesEnabled is false
            public override void DoBrainUpdate()
            {
                //switch direction
                _goingForward = !_goingForward;
                SubInstance.PrintWarning(transform.position.ToString());
            }

            //won't be called if BodyUpdatesEnabled is false
            public override void DoBodyUpdate()
            {
                //update body.
                //what you do here is entirely up to you.
                //I make it move in a circle, for instance.

                //this is executed on every single frame, so keep the expensive stuff (like raycasting, iterating over massive structures etc) in DoBrainUpdate instead
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

            //continue loading your plugin, everything ok
            //this is where your OnServerInitialised logic that depends on other child plugins would go
        }

        public override void OnPluginPreProcessedRegistration()
        {
            //register as many Type generic/prefab/skinID combos as you like with 1 line
            PreProcessedServer.RegisterDronePrefab<DroneCustomExample>(PREFAB_EXAMPLE, SKINID_EXAMPLE);

            //and that's it! now you can just do GameManager.server.CreateEntity(PREFAB_EXAMPLE) from a plugin
            //or just type: spawn drone.example
            //in your server/F1 console, it's that easy!
        }

        public override void OnPluginPreProcessedUnregistration()
        {
            //make sure to unregister everything you've registered before, just provide the prefab name
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
