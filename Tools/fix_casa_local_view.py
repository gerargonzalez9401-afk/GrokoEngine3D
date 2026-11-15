import bpy

fixed = 0
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type != 'VIEW_3D':
            continue
        space = area.spaces.active
        if space.local_view is None:
            continue
        region = next((r for r in area.regions if r.type == 'WINDOW'), None)
        try:
            with bpy.context.temp_override(screen=screen, area=area, region=region, space_data=space):
                bpy.ops.view3d.localview(frame_selected=False)
            fixed += 1
        except Exception as error:
            # Restaurar solamente ese editor elimina el aislamiento guardado sin tocar
            # mallas, colecciones, materiales ni transformaciones de la escena.
            print('Local View operator failed; resetting viewport:', error)
            area.type = 'TEXT_EDITOR'
            area.type = 'VIEW_3D'
            fixed += 1

bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print('CASA_LOCAL_VIEW_DISABLED', fixed, bpy.data.filepath)
