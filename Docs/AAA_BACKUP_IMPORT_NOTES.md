# AAA backup import notes

Fuente revisada:

`D:\Salvas del motor\GrokoEngine_AAA_FINAL_PRO\GrokoEngine`

## Piezas importadas

- `GpuTimerQuery`: medición real de tiempo GPU con triple buffer, inicializada de forma perezosa cuando ya existe contexto OpenGL.
- Integración en Profiler: nueva métrica `GPU Scene` dentro del bloque Render.
- `verify_static.py`: puerta estática rápida sin .NET.
- `verify_pro.ps1`: verificación profesional Debug por defecto, con opciones para Release/Publish/Smoke.

## Pieza útil pendiente

`SceneRenderTarget.Temporal.cs` del backup tiene una base de TAA/acumulación temporal, pero no se importó todavía porque el proyecto actual no tiene `TemporalCameraState` ni el pipeline completo de velocity/motion history conectado a `SceneRenderTarget`.

Importarlo sin esos contratos rompería compilación o quedaría como código muerto. El corte correcto es hacerlo como feature completa:

1. Estado temporal por cámara.
2. Velocity texture real desde Deferred/G-buffer.
3. Reset de historial en resize, teleport y cambios de cámara.
4. Selector de Anti-aliasing TAA en cámara/Render Pro.
5. Tests/escena visual para ghosting y movimiento rápido.
