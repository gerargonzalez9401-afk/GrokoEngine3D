import bpy

# La cubierta sigue existiendo para export/render exterior, pero se oculta en el
# viewport de edición: al abrir la escena se puede trabajar dentro de la casa.
roof = bpy.data.collections.get('02_Roof')
if roof is not None:
    roof.name = '02_Roof (enable for exterior)'
    roof.hide_viewport = True
    roof.hide_render = False

bpy.context.scene['DefaultViewport'] = 'Interior layout - roof hidden; enable 02_Roof for exterior'
bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print('HOUSE_INTERIOR_VIEW_READY', bpy.data.filepath)
