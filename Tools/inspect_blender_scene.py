import bpy

print('FILE', bpy.data.filepath)
for collection in bpy.data.collections:
    print('COLLECTION', collection.name, 'viewport_hidden=', collection.hide_viewport,
          'render_hidden=', collection.hide_render, 'objects=', len(collection.objects))
for obj in bpy.context.scene.objects:
    print('OBJECT', obj.name, 'type=', obj.type, 'viewport_hidden=', obj.hide_viewport,
          'effective_hidden=', obj.hide_get(), 'location=', tuple(round(v, 3) for v in obj.location),
          'scale=', tuple(round(v, 3) for v in obj.scale), 'collections=', ','.join(c.name for c in obj.users_collection))

def visit(layer, indent=''):
    print('LAYER_COLLECTION', indent + layer.name, 'excluded=', layer.exclude, 'hidden=', layer.hide_viewport)
    for child in layer.children:
        visit(child, indent + '  ')

for view_layer in bpy.context.scene.view_layers:
    print('VIEW_LAYER', view_layer.name)
    visit(view_layer.layer_collection)

for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            space = area.spaces.active
            print('VIEWPORT', screen.name, 'local_view=', space.local_view is not None,
                  'clip_start=', space.clip_start, 'clip_end=', space.clip_end,
                  'region_3d_view_distance=', getattr(space.region_3d, 'view_distance', None))
