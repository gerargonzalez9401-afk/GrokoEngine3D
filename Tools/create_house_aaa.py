import bpy
import math
import os

OUT_BLEND = r"C:\Users\Gerar\OneDrive\Documentos\GrokoProjects\nuevoss\Assets\Environment\Casa_AAA_6x8_2Cuartos.blend"
OUT_OBJ = r"C:\Users\Gerar\OneDrive\Documentos\GrokoProjects\nuevoss\Assets\Environment\Casa_AAA_6x8_2Cuartos.obj"

# Escala de producción: 1 unidad = 1 metro. Altura de muro = 3.0 m.
WALL, HEIGHT = 0.20, 3.00


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for c in list(bpy.data.collections):
        bpy.data.collections.remove(c)


def make_collection(name):
    c = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(c)
    return c


def move_to(obj, coll):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    coll.objects.link(obj)


def pbr_material(name, base_a, base_b, roughness, metallic=0.0, noise_scale=3.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nodes, links = m.node_tree.nodes, m.node_tree.links
    for n in nodes: nodes.remove(n)
    out = nodes.new('ShaderNodeOutputMaterial')
    bsdf = nodes.new('ShaderNodeBsdfPrincipled')
    noise = nodes.new('ShaderNodeTexNoise')
    ramp = nodes.new('ShaderNodeValToRGB')
    noise.inputs['Scale'].default_value = noise_scale
    noise.inputs['Detail'].default_value = 4.0
    noise.inputs['Roughness'].default_value = 0.7
    ramp.color_ramp.elements[0].position = 0.28
    ramp.color_ramp.elements[0].color = (*base_a, 1.0)
    ramp.color_ramp.elements[1].position = 0.72
    ramp.color_ramp.elements[1].color = (*base_b, 1.0)
    bsdf.inputs['Roughness'].default_value = roughness
    bsdf.inputs['Metallic'].default_value = metallic
    links.new(noise.outputs['Fac'], ramp.inputs['Fac'])
    links.new(ramp.outputs['Color'], bsdf.inputs['Base Color'])
    links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
    return m


def glass_material():
    m = bpy.data.materials.new('Glass - architectural blue')
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (0.055, 0.18, 0.27, 1)
    p.inputs['Metallic'].default_value = 0.10
    p.inputs['Roughness'].default_value = 0.08
    p.inputs['Transmission Weight'].default_value = 0.72
    return m


def box(name, center, size, mat, coll, bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(location=center)
    o = bpy.context.object
    o.name = name
    o.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        b = o.modifiers.new('Edge bevel', 'BEVEL')
        b.width, b.segments = bevel, 2
    o.data.materials.append(mat)
    move_to(o, coll)
    return o


def wall_x(name, x0, x1, y, coll, mat, h=HEIGHT):
    return box(name, ((x0+x1)*.5, y, h*.5), (max(.02, x1-x0), WALL, h), mat, coll)


def wall_y(name, x, y0, y1, coll, mat, h=HEIGHT):
    return box(name, (x, (y0+y1)*.5, h*.5), (WALL, max(.02, y1-y0), h), mat, coll)


def door(name, x, y, coll, frame_mat, door_mat, facing='x'):
    # Puerta de 0.90 x 2.10 m con marco arquitectónico; panel cerrado y alineado.
    w, h, d = .90, 2.10, .10
    if facing == 'x':
        box(name+'_frame_L', (x-w*.5-WALL*.5, y, h*.5), (WALL, WALL*1.8, h), frame_mat, coll, .01)
        box(name+'_frame_R', (x+w*.5+WALL*.5, y, h*.5), (WALL, WALL*1.8, h), frame_mat, coll, .01)
        box(name+'_frame_top', (x, y, h+WALL*.5), (w+WALL*2, WALL*1.8, WALL), frame_mat, coll, .01)
        panel = box(name+'_panel', (x, y-WALL*.10, h*.5), (w, d, h), door_mat, coll, .025)
        # Sutileza de panel de puerta, sigue siendo arquitectura y no mobiliario.
        box(name+'_recess', (x, y-d*.56, h*.62), (w*.68, .012, h*.56), frame_mat, coll, .01)
    else:
        box(name+'_frame_L', (x, y-w*.5-WALL*.5, h*.5), (WALL*1.8, WALL, h), frame_mat, coll, .01)
        box(name+'_frame_R', (x, y+w*.5+WALL*.5, h*.5), (WALL*1.8, WALL, h), frame_mat, coll, .01)
        box(name+'_frame_top', (x, y, h+WALL*.5), (WALL*1.8, w+WALL*2, WALL), frame_mat, coll, .01)
        box(name+'_panel', (x-WALL*.10, y, h*.5), (d, w, h), door_mat, coll, .025)


def window_x(name, x, y, width, coll, frame, glass):
    # Ventana de fachada (pared paralela a X): marco profundo, dintel, alféizar y montante.
    z, wh = 1.60, 1.35
    box(name+'_glass', (x, y, z), (width, .028, wh), glass, coll)
    box(name+'_sill', (x, y-.04, z-wh*.5-.06), (width+.16, .22, .12), frame, coll, .015)
    box(name+'_top', (x, y, z+wh*.5), (width+.16, WALL*1.55, .12), frame, coll, .012)
    box(name+'_left', (x-width*.5, y, z), (.12, WALL*1.55, wh), frame, coll, .012)
    box(name+'_right', (x+width*.5, y, z), (.12, WALL*1.55, wh), frame, coll, .012)
    box(name+'_mullion', (x, y, z), (.075, WALL*1.35, wh-.10), frame, coll)


def window_y(name, x, y, width, coll, frame, glass):
    z, wh = 1.60, 1.35
    box(name+'_glass', (x, y, z), (.028, width, wh), glass, coll)
    box(name+'_sill', (x-.04, y, z-wh*.5-.06), (.22, width+.16, .12), frame, coll, .015)
    box(name+'_top', (x, y, z+wh*.5), (WALL*1.55, width+.16, .12), frame, coll, .012)
    box(name+'_left', (x, y-width*.5, z), (WALL*1.55, .12, wh), frame, coll, .012)
    box(name+'_right', (x, y+width*.5, z), (WALL*1.55, .12, wh), frame, coll, .012)
    box(name+'_mullion', (x, y, z), (WALL*1.35, .075, wh-.10), frame, coll)


clear_scene()
arch = make_collection('01_Architecture')
roof = make_collection('02_Roof')
presentation = make_collection('03_Presentation')

plaster = pbr_material('PBR Plaster - warm ivory', (0.36, .32, .27), (.70, .65, .56), .83, noise_scale=5.5)
concrete = pbr_material('PBR Concrete - charcoal', (.055, .064, .071), (.16, .18, .19), .48, metallic=.02, noise_scale=7.5)
wood = pbr_material('PBR Oak Door', (.035, .010, .004), (.22, .070, .018), .30, noise_scale=2.2)
frame = pbr_material('PBR Window Frame - black anodized', (.007, .009, .011), (.045, .052, .057), .25, metallic=.72, noise_scale=12)
glass = glass_material()
roof_mat = pbr_material('PBR Roof membrane', (.018, .022, .025), (.085, .092, .098), .68, noise_scale=9)

# Planta: 14 x 12 m. El salón principal conserva 8 x 6 m; techo útil a 3 m.
# Sala: X[-7,1], Y[-6,0]. Pasillo de 1.5 m; dos cuartos y baño detrás.
box('Foundation_Slab', (0, 0, -.10), (14.0, 12.0, .20), concrete, arch)
box('Interior_Floor_Finish', (0, 0, .011), (13.56, 11.56, .022), pbr_material('PBR Interior Floor', (.12,.13,.135), (.26,.28,.29), .37, noise_scale=14), arch)

# Fachada: ventana amplia + puerta real con muros segmentados, sin intersecciones.
wall_x('Front_Facade_A', -7.0, -5.15, -6.0, arch, plaster)
wall_x('Front_Facade_B', -2.85, -0.58, -6.0, arch, plaster)
wall_x('Front_Facade_C', 0.58, 7.0, -6.0, arch, plaster)
window_x('Front_Window', -4.0, -6.0, 2.30, arch, frame, glass)
door('Main_Entrance', 0.0, -6.0, arch, frame, wood, 'x')

# Laterales y trasera: cada ventana tiene hueco físico más marco y alféizar.
wall_y('West_Wall_A', -7.0, -6.0, -2.85, arch, plaster)
wall_y('West_Wall_B', -7.0, -1.15, 6.0, arch, plaster)
window_y('Living_Room_Window', -7.0, -2.0, 1.60, arch, frame, glass)
wall_y('East_Wall_A', 7.0, -6.0, 1.95, arch, plaster)
wall_y('East_Wall_B', 7.0, 4.05, 6.0, arch, plaster)
window_y('Bedroom2_Window', 7.0, 3.0, 1.80, arch, frame, glass)
wall_x('Rear_Wall_A', -7.0, -5.15, 6.0, arch, plaster)
wall_x('Rear_Wall_B', -2.85, 1.10, 6.0, arch, plaster)
wall_x('Rear_Wall_C', 3.00, 7.0, 6.0, arch, plaster)
window_x('Bedroom1_Window', -4.0, 6.0, 2.30, arch, frame, glass)
window_x('Bathroom_Window', 2.05, 6.0, 1.70, arch, frame, glass)

# Corredor de 1.5 m y distribución privada: 2 cuartos + baño.
# Muro de acceso a habitaciones en Y=1.5, segmentado con puertas de 0.9 m.
wall_x('Private_Wall_A', -7.0, -5.15, 1.5, arch, plaster)
wall_x('Private_Wall_B', -4.25, -1.95, 1.5, arch, plaster)
wall_x('Private_Wall_C', -1.05, -.45, 1.5, arch, plaster)
wall_x('Private_Wall_D', .45, 3.55, 1.5, arch, plaster)
wall_x('Private_Wall_E', 4.45, 7.0, 1.5, arch, plaster)
door('Bedroom1_Door', -4.70, 1.5, arch, frame, wood, 'x')
door('Bathroom_Door', -1.50, 1.5, arch, frame, wood, 'x')
door('Bedroom2_Door', 4.00, 1.5, arch, frame, wood, 'x')

# Baño central 3 x 4.5 m, separado limpiamente de ambos cuartos.
wall_y('Bedroom1_Bath_Partition', -.45, 1.5, 6.0, arch, plaster)
wall_y('Bath_Bedroom2_Partition', .45, 1.5, 6.0, arch, plaster)

# Zócalo oscuro continuo: disimula la unión piso/muro y da terminación AAA.
wall_x('Skirting_Front', -6.90, 6.90, -5.86, arch, concrete, .16)
wall_x('Skirting_Back', -6.90, 6.90, 5.86, arch, concrete, .16)
wall_y('Skirting_West', -6.86, -5.85, 5.85, arch, concrete, .16)
wall_y('Skirting_East', 6.86, -5.85, 5.85, arch, concrete, .16)

# Paños de fachada en concreto oscuro: modulan la fachada, enmarcan accesos y
# ventanas sin añadir decoración ni mobiliario.
box('Facade_Window_Accent', (-4.0, -6.115, 1.60), (3.05, .045, 1.82), concrete, arch, .01)
box('Facade_Door_Accent', (0.0, -6.118, 1.55), (1.42, .045, 2.72), concrete, arch, .01)
# Se vuelven a colocar vidrio y marco por delante de los paños de acento.
window_x('Front_Window_Finish', -4.0, -6.155, 2.30, arch, frame, glass)
door('Main_Entrance_Finish', 0.0, -6.160, arch, frame, wood, 'x')

# Techo plano moderno: losa, parapeto y remate continuo. Sin huecos entre pared y cubierta.
box('Roof_Slab', (0, 0, 3.08), (14.36, 12.36, .22), roof_mat, roof)
box('Parapet_Front', (0, -6.10, 3.40), (14.36, .20, .50), concrete, roof, .015)
box('Parapet_Back', (0, 6.10, 3.40), (14.36, .20, .50), concrete, roof, .015)
box('Parapet_West', (-7.10, 0, 3.40), (.20, 12.20, .50), concrete, roof, .015)
box('Parapet_East', (7.10, 0, 3.40), (.20, 12.20, .50), concrete, roof, .015)

# Cámara y luz sólo para presentar el asset; la exportación OBJ selecciona arquitectura/techo.
bpy.ops.object.light_add(type='SUN', location=(0, 0, 8))
sun = bpy.context.object
sun.name = 'Presentation_Sun'
sun.data.energy = 2.0
sun.rotation_euler = (math.radians(24), math.radians(-18), math.radians(-28))
move_to(sun, presentation)
bpy.ops.object.light_add(type='AREA', location=(-4, -5, 7))
area = bpy.context.object
area.name = 'Presentation_Sky_Fill'
area.data.energy, area.data.shape, area.data.size = 900, 'DISK', 7
area.data.color = (.55, .68, 1.0)
move_to(area, presentation)
bpy.ops.object.camera_add(location=(17.5, -20.5, 14.0))
cam = bpy.context.object
cam.name = 'Presentation_Camera'
cam.data.lens = 38
target = bpy.data.objects.new('Presentation_Target', None)
target.location = (0, 0, 1.2)
presentation.objects.link(target)
track = cam.constraints.new('TRACK_TO')
track.target, track.track_axis, track.up_axis = target, 'TRACK_NEGATIVE_Z', 'UP_Y'
move_to(cam, presentation)

scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.length_unit = 'METERS'
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
scene.render.resolution_percentage = 50
scene.world.color = (.018, .024, .032)
scene.camera = cam

os.makedirs(os.path.dirname(OUT_BLEND), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)

bpy.ops.object.select_all(action='DESELECT')
for group in (arch, roof):
    for obj in group.objects:
        if obj.type == 'MESH': obj.select_set(True)
try:
    bpy.context.view_layer.objects.active = bpy.data.objects['Foundation_Slab']
    bpy.ops.wm.obj_export(filepath=OUT_OBJ, export_materials=True, export_selected_objects=True)
except Exception as error:
    print('OBJ export skipped:', error)

print('AAA_HOUSE_CREATED', OUT_BLEND)
print('AAA_HOUSE_OBJ', OUT_OBJ)
