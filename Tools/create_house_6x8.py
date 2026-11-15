import bpy
import math
import os

OUT_BLEND = r"C:\Users\Gerar\OneDrive\Documentos\GrokoProjects\nuevoss\Assets\Environment\Casa_6x8_2Cuartos.blend"
OUT_OBJ = r"C:\Users\Gerar\OneDrive\Documentos\GrokoProjects\nuevoss\Assets\Environment\Casa_6x8_2Cuartos.obj"

# Escala: 1 unidad Blender = 1 metro. El salón mide 8 x 6 m y el techo 3 m.
WALL = 0.18
H = 3.0


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)


def collection(name):
    c = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(c)
    return c


def material(name, color, metallic=0.0, roughness=0.55):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1.0)
    p.inputs['Metallic'].default_value = metallic
    p.inputs['Roughness'].default_value = roughness
    return m


def box(name, loc, size, mat, coll, bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(location=loc)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0:
        mod = obj.modifiers.new('Soft edges', 'BEVEL')
        mod.width = bevel
        mod.segments = 2
    obj.data.materials.append(mat)
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    coll.objects.link(obj)
    return obj


def wall_x(name, x0, x1, y, coll, mat):
    return box(name, ((x0+x1)/2, y, H/2), (max(0.05, x1-x0), WALL, H), mat, coll)


def wall_y(name, x, y0, y1, coll, mat):
    return box(name, (x, (y0+y1)/2, H/2), (WALL, max(0.05, y1-y0), H), mat, coll)


def door_frame(name, x, y, coll, wood, facing='x'):
    # abertura 0.9 x 2.1 m, con puerta ligeramente abierta.
    opening, dh = 0.9, 2.1
    if facing == 'x':
        box(name + '_Jamb_L', (x-opening/2-WALL/2, y, dh/2), (WALL, WALL*1.6, dh), wood, coll)
        box(name + '_Jamb_R', (x+opening/2+WALL/2, y, dh/2), (WALL, WALL*1.6, dh), wood, coll)
        box(name + '_Lintel', (x, y, dh+WALL/2), (opening+WALL*2, WALL*1.6, WALL), wood, coll)
        panel = box(name + '_Door', (x+0.21, y+0.12, dh/2), (opening, 0.055, dh), wood, coll, 0.02)
        panel.rotation_euler[2] = math.radians(-26)
    else:
        box(name + '_Jamb_L', (x, y-opening/2-WALL/2, dh/2), (WALL*1.6, WALL, dh), wood, coll)
        box(name + '_Jamb_R', (x, y+opening/2+WALL/2, dh/2), (WALL*1.6, WALL, dh), wood, coll)
        box(name + '_Lintel', (x, y, dh+WALL/2), (WALL*1.6, opening+WALL*2, WALL), wood, coll)
        panel = box(name + '_Door', (x+0.12, y+0.21, dh/2), (0.055, opening, dh), wood, coll, 0.02)
        panel.rotation_euler[2] = math.radians(26)


def window(name, x, y, coll, frame, glass, facing='x'):
    # Vidrio luminoso dentro de una pared ya dividida en segmentos.
    z, sx, sz = 1.65, 1.35, 1.15
    if facing == 'x':
        box(name + '_Glass', (x, y, z), (sx, 0.025, sz), glass, coll)
        box(name + '_Top', (x, y, z+sz/2), (sx+0.12, WALL*1.3, 0.10), frame, coll)
        box(name + '_Bottom', (x, y, z-sz/2), (sx+0.12, WALL*1.3, 0.10), frame, coll)
        box(name + '_Left', (x-sx/2, y, z), (0.10, WALL*1.3, sz), frame, coll)
        box(name + '_Right', (x+sx/2, y, z), (0.10, WALL*1.3, sz), frame, coll)
    else:
        box(name + '_Glass', (x, y, z), (0.025, sx, sz), glass, coll)
        box(name + '_Top', (x, y, z+sz/2), (WALL*1.3, sx+0.12, 0.10), frame, coll)
        box(name + '_Bottom', (x, y, z-sz/2), (WALL*1.3, sx+0.12, 0.10), frame, coll)
        box(name + '_Left', (x, y-sx/2, z), (WALL*1.3, 0.10, sz), frame, coll)
        box(name + '_Right', (x, y+sx/2, z), (WALL*1.3, 0.10, sz), frame, coll)


def label(text, loc, coll):
    bpy.ops.object.text_add(location=loc, rotation=(0, 0, 0))
    obj = bpy.context.object
    obj.name = 'Label_' + text
    obj.data.body = text
    obj.data.align_x = 'CENTER'
    obj.data.size = 0.42
    obj.data.extrude = 0.008
    obj.data.materials.append(label_mat)
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    coll.objects.link(obj)
    return obj


clear_scene()
architecture = collection('01_ARCHITECTURE')
furniture = collection('02_FURNITURE')
lights = collection('03_LIGHTING')
markers = collection('04_LABELS')
roof = collection('05_ROOF_HIDDEN')

wall_mat = material('Wall - warm plaster', (0.63, 0.59, 0.53), 0.0, 0.82)
floor_mat = material('Floor - polished concrete', (0.18, 0.20, 0.22), 0.04, 0.42)
wood = material('Wood - walnut', (0.16, 0.055, 0.022), 0.0, 0.38)
fabric = material('Fabric - blue grey', (0.10, 0.19, 0.26), 0.0, 0.75)
bedding = material('Bedding - clean', (0.63, 0.68, 0.70), 0.0, 0.83)
ceramic = material('Ceramic', (0.78, 0.81, 0.82), 0.0, 0.26)
glass = material('Glass - blue', (0.08, 0.28, 0.42), 0.15, 0.12)
label_mat = material('Labels', (0.95, 0.75, 0.24), 0.0, 0.5)
ceiling_mat = material('Roof', (0.21, 0.22, 0.24), 0.0, 0.78)

# Planta total: 12 x 11 m. Salón = 8 x 6 m (X -6 a 2, Y -5.5 a 0.5).
box('Floor_Slab', (0, 0, -0.08), (12.0, 11.0, 0.16), floor_mat, architecture)

# Fachada y exteriores, con hueco de puerta principal y dos ventanas.
wall_x('Front_Wall_Left', -6.0, -0.55, -5.5, architecture, wall_mat)
wall_x('Front_Wall_Right', 0.55, 6.0, -5.5, architecture, wall_mat)
door_frame('Front_Door', 0, -5.5, architecture, wood, 'x')
wall_x('Back_Wall', -6.0, 6.0, 5.5, architecture, wall_mat)
wall_y('Left_Wall_Front', -6.0, -5.5, -1.2, architecture, wall_mat)
wall_y('Left_Wall_Back', -6.0, 1.2, 5.5, architecture, wall_mat)
window('Living_Window', -6.0, 0.0, architecture, wood, glass, 'y')
wall_y('Right_Wall_Front', 6.0, -5.5, -1.0, architecture, wall_mat)
wall_y('Right_Wall_Back', 6.0, 1.0, 5.5, architecture, wall_mat)
window('Bedroom_Window', 6.0, 0.0, architecture, wood, glass, 'y')

# División salón / zona privada con tres puertas: baño, cuarto 1, cuarto 2.
wall_x('PrivateDivider_A', -6.0, -5.15, 0.5, architecture, wall_mat)
wall_x('PrivateDivider_B', -4.25, -2.0, 0.5, architecture, wall_mat)
wall_x('PrivateDivider_C', -1.1, 1.0, 0.5, architecture, wall_mat)
wall_x('PrivateDivider_D', 1.9, 6.0, 0.5, architecture, wall_mat)
door_frame('Bath_Door', -4.7, 0.5, architecture, wood, 'x')
door_frame('Bedroom1_Door', -1.55, 0.5, architecture, wood, 'x')
door_frame('Bedroom2_Door', 1.45, 0.5, architecture, wood, 'x')

# Baño 3 x 3, cuarto 1 de 4.5 x 5, cuarto 2 de 4.5 x 5.
wall_y('Bath_Bedroom1_Wall', -3.0, 0.5, 3.2, architecture, wall_mat)
wall_y('Bath_Bedroom1_Wall_2', -3.0, 4.1, 5.5, architecture, wall_mat)
door_frame('Bathroom_Internal_Door', -3.0, 3.65, architecture, wood, 'y')
wall_y('BedroomDivider', 1.5, 0.5, 5.5, architecture, wall_mat)

# Techo separado y oculto en el viewport: se activa al exportar un exterior cerrado.
box('Flat_Roof', (0, 0, H + 0.10), (12.35, 11.35, 0.20), ceiling_mat, roof)
roof.hide_viewport = True
roof.hide_render = True

# Sala: sofá, mesa y mueble de TV.
box('Living_Sofa_Base', (-2.8, -2.9, 0.38), (3.3, 0.95, 0.42), fabric, furniture, 0.08)
box('Living_Sofa_Back', (-2.8, -3.30, 0.86), (3.3, 0.16, 0.72), fabric, furniture, 0.05)
box('Living_Coffee_Table', (-0.7, -2.3, 0.38), (1.35, 0.72, 0.40), wood, furniture, 0.04)
box('Living_TV_Unit', (1.25, -4.35, 0.46), (1.9, 0.38, 0.92), wood, furniture, 0.03)

# Cuarto 1.
box('Bedroom1_Bed_Base', (-0.65, 3.45, 0.30), (2.05, 1.65, 0.42), wood, furniture, 0.05)
box('Bedroom1_Mattress', (-0.65, 3.45, 0.60), (1.95, 1.55, 0.22), bedding, furniture, 0.05)
box('Bedroom1_Headboard', (-0.65, 4.18, 1.02), (2.05, 0.10, 0.82), wood, furniture)
box('Bedroom1_Desk', (0.45, 1.35, 0.48), (1.4, 0.58, 0.76), wood, furniture, 0.03)

# Cuarto 2.
box('Bedroom2_Bed_Base', (3.75, 3.5, 0.30), (1.70, 2.10, 0.42), wood, furniture, 0.05)
box('Bedroom2_Mattress', (3.75, 3.5, 0.60), (1.60, 2.00, 0.22), bedding, furniture, 0.05)
box('Bedroom2_Wardrobe', (5.05, 4.35, 1.15), (0.62, 1.45, 2.25), wood, furniture, 0.03)

# Baño.
box('Bath_Vanity', (-4.65, 4.70, 0.48), (1.15, 0.52, 0.86), wood, furniture, 0.02)
box('Bath_Sink', (-4.65, 4.70, 0.96), (0.72, 0.38, 0.12), ceramic, furniture, 0.05)
box('Bath_Toilet_Base', (-5.05, 2.05, 0.35), (0.60, 0.72, 0.70), ceramic, furniture, 0.10)
box('Bath_Shower_Glass', (-3.72, 1.72, 1.15), (0.03, 1.42, 2.25), glass, furniture)

# Etiquetas sobre el suelo, útiles para comprobar escala y distribución.
label('SALA 8 x 6 m', (-2.0, -0.75, 0.012), markers)
label('BAÑO 3 x 3 m', (-4.5, 3.3, 0.012), markers)
label('CUARTO 1', (-0.7, 1.20, 0.012), markers)
label('CUARTO 2', (3.75, 1.20, 0.012), markers)

# Luz suave de presentación; no representa luces del juego.
def area(name, loc, energy, size, color):
    bpy.ops.object.light_add(type='AREA', location=loc)
    o = bpy.context.object
    o.name = name
    o.data.energy = energy
    o.data.shape = 'DISK'
    o.data.size = size
    o.data.color = color
    o.rotation_euler = (0, 0, 0)
    for c in list(o.users_collection): c.objects.unlink(o)
    lights.objects.link(o)
    return o

area('Presentation_Key', (-2.0, -1.0, 4.8), 1200, 5.0, (1.0, 0.76, 0.55))
area('Presentation_Fill', (3.5, 1.0, 4.5), 900, 4.0, (0.52, 0.68, 1.0))

bpy.ops.object.light_add(type='SUN', location=(0, 0, 5))
sun = bpy.context.object
sun.name = 'Presentation_Sun'
sun.data.energy = 1.2
sun.rotation_euler = (math.radians(32), math.radians(-18), math.radians(25))
for c in list(sun.users_collection): c.objects.unlink(sun)
lights.objects.link(sun)

# Cámara elevada de layout.
bpy.ops.object.camera_add(location=(12.5, -15.0, 13.0))
cam = bpy.context.object
cam.name = 'Layout_Camera'
cam.data.lens = 30
direction = bpy.data.objects.new('Camera_Target', None)
direction.location = (0, 0, 0)
markers.objects.link(direction)
constraint = cam.constraints.new(type='TRACK_TO')
constraint.target = direction
constraint.track_axis = 'TRACK_NEGATIVE_Z'
constraint.up_axis = 'UP_Y'
bpy.context.scene.camera = cam

scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.length_unit = 'METERS'
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 1920
scene.render.resolution_y = 1080
scene.render.resolution_percentage = 50
scene.world.color = (0.035, 0.045, 0.065)

os.makedirs(os.path.dirname(OUT_BLEND), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)

# OBJ permite importación inmediata al motor; el .blend conserva toda la edición.
bpy.ops.object.select_all(action='DESELECT')
for c in (architecture, furniture):
    for o in c.objects:
        if o.type == 'MESH':
            o.select_set(True)
try:
    bpy.context.view_layer.objects.active = bpy.data.objects.get('Floor_Slab')
    bpy.ops.wm.obj_export(filepath=OUT_OBJ, export_materials=True, export_selected_objects=True)
except Exception as e:
    print('OBJ export skipped:', e)

print('HOUSE_CREATED', OUT_BLEND)
print('HOUSE_OBJ', OUT_OBJ)
