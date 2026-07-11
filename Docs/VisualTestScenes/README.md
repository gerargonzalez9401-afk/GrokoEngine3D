# GrokoEngine Visual Test Scenes

Escenas pequeñas para validar visualmente cambios de render sin tocar proyectos reales.

- `01_pbr_materials.gscene`: materiales base, metallic/roughness/emission.
- `02_camera_pipeline.gscene`: cámara con postprocess, depth/opaque texture y layers.
- `03_transparency_particles.gscene`: material transparente/refraction y partículas.
- `04_terrain_cdlod.gscene`: terreno básico para validar CDLOD, splat/normal bake y sombras.

Uso recomendado:

1. Copia una escena a un proyecto dentro de `Assets/Scenes`.
2. Ábrela desde el editor.
3. Compara FPS/profiler y look antes/después de tocar render.

Estas escenas son fixtures de desarrollo: simples a propósito, sin assets pesados ni rutas externas.
