## Introduction

Custom Drones is a hard dependency for child plugins and as such it does not add any specific types of Drones to Rust on its own. Neither does it contain any configuration (subject to change), permissions, GUI nor commands - those are left open to interpretation by child plugins. It contains stored data.

It does, however, provide a base for developers to easily extend the *DroneCustomBasic* class with an arbitrary implementation of their choice - and then let them register their own prefab path for a spawnable prototype of their extended entity. Registering a path with the GameManager and the GameManifest means that any plugin, admin, or even the server command "spawn" will treat that prototype just like vanilla entity prefabs would be treated, and that prefab will show up on the list of spawnable entity prefabs. As long as the plugin is still loaded in!

Custom Drones does all the heavy lifting associated with that seemingly impossible feat. It hides the unimaginable hackery involved to make it work behind a comfortable curtain of abstraction - so the developers get to focus on the fun bits.

After unloading, everything goes back to normal. Custom Drone entities are serialised, stored in the plugin datafile and killed - to avoid Null reference errors for no-longer-existing Types. They can be restored later on plugin reload.  Any changes made to the GameManager/GameManifest are reverted.

NOTE: This plugin utilises Reflection in 1 or 2 places in order to force the BaseEntity._prefabName field to its custom, registered prefab name, instead of the prefab name of the original entity it was based on. This field is private and changing its accessor to public leads to less than stellar results, according to WhiteThunder's experiments with patching Oxide. In my opinion there is no point in turning the entire plugin into a compiled extension for the sole purpose of addressing the BaseEntity._prefabName field.

## Functionality handled internally

* Registering and unregistering spawnable prefabs of custom Drone implementations: just provide your Type as a generic, along with your preferred prefab name and a unique skin ID, and you’re done!
* All drones of particular type and their pickup Items will always have a unique skin ID associated with their Type
* Loading arbitrary drone data from plugin datafile (which can be wiped at any time) on plugin load
* Saving arbitrary drone data to plugin datafile on server save and plugin unload
* When a custom drone is picked up, its data is also saved and the pickup Item is now permanently tied to that data
* When a custom Drone is deployed back from an Item, and it’s tied to existing Drone data, the Drone loads the arbitrary data back and it assumes the owner ID of the player that has just deployed it
* When a custom Drone Item is removed from the world, and that Item was tied to existing Drone data, that existing Drone data is permanently removed (except when the Item dies right after deploying the Drone from it - in that case, the data persists)
* Likewise, if a Drone is killed (and it’s not happening on plugin unload), its data is also permanently removed from the datafile. On plugin unload, the data is saved instead

## Stored permadata (oxide/data/CustomDrones.PERMADATA.json)

At the moment it contains just two fields: LastDataID, which gets incremented every time a new drone entity is spawned, and Hash (dictionary) of drone data entries by their unique ID.
Clearing the data (AFTER the plugin is unloaded, otherwise it will get overwritten on Unload) will permanently remove all custom drones.

Each entry in the data has contains the following fields
* int **EntryID**: the unique ID of the data, same as the key of the Hash entry
* bool **IsItem**: whether this data belongs to a drone that exists in the world, or was that drone picked up as an item?
* byte[] **RawData**: a byte array with the size of 256 bytes (subject to change in the future). Read the section below…

## RawData
In order to allow any type of Drone to save/load arbitrary data in any possible way, while being completely agnostic to the Type, every possible piece of information about the drone that needs to persist between reloads is saved to a buffer in a consistent, predictable manner.

The first 80 bytes are reserved for the following properties, so you don’t have to worry about them:

* uint **FakePrefabID** [4 bytes, used internally to identify what prototype to clone]
* ulong **OwnerID** [16 bytes, indicates who deployed the drone last, 0 if none]
* float **healthFraction** [4 bytes, indicates the health left out of max 100]
* float **transform.position.x** [4 bytes, indicates the last x-component of the entity position]
* float **transform.position.y** [4 bytes, indicates the last y-component of the entity position]
* float **transform.position.z** [4 bytes, indicates the last z-component of the entity position]
* float **transform.eulerAngles.x** [4 bytes, indicates the last x-component of the entity euler rotation]
* float **transform.eulerAngles.y** [4 bytes, indicates the last x-component of the entity euler rotation]
* float **transform.eulerAngles.z** [4 bytes, indicates the last x-component of the entity euler rotation]

The remaining 176 bytes are free to store anything else you might want to keep track of. The example plugin included shows exactly how that can be achieved easily.

## Stored metadata (oxide/data/CustomDrones.METADATA.json)

As we know, Oxide doesn’t like multi-part plugins. Custom Drones contain a workaround for that nasty situation when you reload/unload either the parent or the child, and the children are stuck in Limbo after you try to load them back in.
To achieve this, every child plugin that registers with Custom Drones is added to the list of seen plugins - so next time Custom Drone is loaded and it notices those plugins missing, it tries to reload them 1 by 1. After that, if some plugins are still missing, CustomDrones won’t proceed, it will print a list of missing plugins and urge the user to resolve the issue manually.

Currently there’s no need to clear this datafile - in the future, child plugins will have a way of “uninstalling” themselves so their metadata entry is removed and their absence won’t cause Custom Drones to fail loading, if the user chooses to permanently get rid of one of the child plugins, but they’d like to keep Custom Drones, still.

## HOW TO: Example child plugin

The best way to convey how to utilise Custom Drones as a dependency for your plugins is by [having a look at the provided example](https://github.com/Nikedemos/Custom-Drones/blob/main/CustomDronesSubExample.cs), which can be viewed/downloaded from [Custom Drones on GitHub](https://github.com/Nikedemos/Custom-Drones).

The important bits of this very light-weight example (around 140 lines of meaningful code) are commented on and explained. The license of the example is the same as the license of Custom Drones, i.e. very permissive.

