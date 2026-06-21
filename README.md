# Path Weavers

## Executing PathWeaver on laptop without VR Headset and MetaXRSimulator

1. GameObject "XR/PC Manager" -> Script Computer XR Manager -> Use XR = false

   > You can also manually deactivate all game objects in the hierarchy list. However, the boolean needs to be set to false.

2. Start Play Mode
3. GameObject PC_StartHost -> Script Network Controller For UI Canvas -> ContextMenu -> Start Host
4. Weaver is following mouse
5. Click & Drag to create roads (spawning of nature and buidings is automatic)
6. GameObject TrafficNetwork -> Script Traffic Network -> ContextMenu -> Spawn Traffic
