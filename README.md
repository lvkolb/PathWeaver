# Path Weavers

## Setup

#### Install 3D Models from Asset Store

- AllSky Free - 10 Sky / Skybox Set
  https://assetstore.unity.com/packages/2d/textures-materials/sky/allsky-free-10-sky-skybox-set-146014

- SimplePoly City - Low Poly Assets
  https://assetstore.unity.com/packages/3d/environments/simplepoly-city-low-poly-assets-58899

- AllSky Free - 10 Sky / Skybox Set
  https://assetstore.unity.com/packages/2d/textures-materials/sky/allsky-free-10-sky-skybox-set-146014

- Low Poly Tree Pack
  https://assetstore.unity.com/packages/3d/vegetation/trees/low-poly-tree-pack-57866#content

- Ignore Trees and Rocks - Low Poly Pack: TinyNature
  https://assetstore.unity.com/packages/3d/environments/landscapes/trees-and-rocks-low-poly-pack-tinynature-130107

- Low Poly Wind
  https://assetstore.unity.com/packages/vfx/shaders/low-poly-wind-182586

- Low Poly Stones
  https://assetstore.unity.com/packages/3d/props/low-poly-stones-298380

- Planes & Choppers - PolyPack
  https://assetstore.unity.com/packages/3d/vehicles/air/planes-choppers-polypack-194946

#### Add Sound

- Sounds (Link for downloading see Credits.md) -> Assets\Sounds

## Executing PathWeaver on laptop without VR Headset and MetaXRSimulator

1. GameObject "XR/PC Manager" -> Script Computer XR Manager -> Use XR = false

   > You can also manually deactivate all game objects in the hierarchy list. However, the boolean needs to be set to false.

2. Start Play Mode
3. GameObject PC_StartHost -> Script Network Controller For UI Canvas -> ContextMenu -> Start Host
4. Weaver is following mouse
5. Click & Drag to create roads (spawning of nature and buidings is automatic)
6. GameObject TrafficNetwork -> Script Traffic Network -> ContextMenu -> Spawn Traffic
