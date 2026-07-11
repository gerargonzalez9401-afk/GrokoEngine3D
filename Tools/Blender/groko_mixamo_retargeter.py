bl_info = {
    "name": "GrokoEngine Mixamo Retargeter",
    "author": "Gerar + Codex",
    "version": (1, 0, 0),
    "blender": (4, 0, 0),
    "location": "View3D > Sidebar > Groko",
    "description": "Retarget and bake Mixamo animations onto custom Blender armatures for GrokoEngine.",
    "category": "Animation",
}

import bpy
import re
from bpy.props import (
    BoolProperty,
    CollectionProperty,
    IntProperty,
    PointerProperty,
    StringProperty,
)
from bpy.types import Operator, Panel, PropertyGroup


MIXAMO_ALIASES = {
    "mixamorig:hips": "hips",
    "mixamorig:spine": "spine",
    "mixamorig:spine1": "chest",
    "mixamorig:spine2": "upperchest",
    "mixamorig:neck": "neck",
    "mixamorig:head": "head",
    "mixamorig:leftshoulder": "leftshoulder",
    "mixamorig:leftarm": "leftupperarm",
    "mixamorig:leftforearm": "leftlowerarm",
    "mixamorig:lefthand": "lefthand",
    "mixamorig:rightshoulder": "rightshoulder",
    "mixamorig:rightarm": "rightupperarm",
    "mixamorig:rightforearm": "rightlowerarm",
    "mixamorig:righthand": "righthand",
    "mixamorig:leftupleg": "leftupperleg",
    "mixamorig:leftleg": "leftlowerleg",
    "mixamorig:leftfoot": "leftfoot",
    "mixamorig:lefttoebase": "lefttoe",
    "mixamorig:rightupleg": "rightupperleg",
    "mixamorig:rightleg": "rightlowerleg",
    "mixamorig:rightfoot": "rightfoot",
    "mixamorig:righttoebase": "righttoe",
}

GENERIC_ALIASES = {
    "pelvis": "hips",
    "root": "hips",
    "hip": "hips",
    "hips": "hips",
    "spine": "spine",
    "spine01": "spine",
    "spine1": "chest",
    "chest": "chest",
    "upperchest": "upperchest",
    "spine02": "chest",
    "spine2": "upperchest",
    "neck": "neck",
    "head": "head",
    "claviclel": "leftshoulder",
    "leftclavicle": "leftshoulder",
    "lclavicle": "leftshoulder",
    "leftshoulder": "leftshoulder",
    "upperarml": "leftupperarm",
    "leftarm": "leftupperarm",
    "leftupperarm": "leftupperarm",
    "lupperarm": "leftupperarm",
    "forearml": "leftlowerarm",
    "leftforearm": "leftlowerarm",
    "leftlowerarm": "leftlowerarm",
    "llowerarm": "leftlowerarm",
    "handl": "lefthand",
    "lefthand": "lefthand",
    "lhand": "lefthand",
    "clavicler": "rightshoulder",
    "rightclavicle": "rightshoulder",
    "rclavicle": "rightshoulder",
    "rightshoulder": "rightshoulder",
    "upperarmr": "rightupperarm",
    "rightarm": "rightupperarm",
    "rightupperarm": "rightupperarm",
    "rupperarm": "rightupperarm",
    "forearmr": "rightlowerarm",
    "rightforearm": "rightlowerarm",
    "rightlowerarm": "rightlowerarm",
    "rlowerarm": "rightlowerarm",
    "handr": "righthand",
    "righthand": "righthand",
    "rhand": "righthand",
    "uplegl": "leftupperleg",
    "thighl": "leftupperleg",
    "leftupleg": "leftupperleg",
    "leftupperleg": "leftupperleg",
    "lthigh": "leftupperleg",
    "legl": "leftlowerleg",
    "shinl": "leftlowerleg",
    "calfl": "leftlowerleg",
    "leftleg": "leftlowerleg",
    "leftlowerleg": "leftlowerleg",
    "footl": "leftfoot",
    "leftfoot": "leftfoot",
    "lfoot": "leftfoot",
    "toel": "lefttoe",
    "lefttoe": "lefttoe",
    "lefttoebase": "lefttoe",
    "uplegr": "rightupperleg",
    "thighr": "rightupperleg",
    "rightupleg": "rightupperleg",
    "rightupperleg": "rightupperleg",
    "rthigh": "rightupperleg",
    "legr": "rightlowerleg",
    "shinr": "rightlowerleg",
    "calfr": "rightlowerleg",
    "rightleg": "rightlowerleg",
    "rightlowerleg": "rightlowerleg",
    "footr": "rightfoot",
    "rightfoot": "rightfoot",
    "rfoot": "rightfoot",
    "toer": "righttoe",
    "righttoe": "righttoe",
    "righttoebase": "righttoe",
    # ── Rigify metarig / rigs estilo "shoulder.L", "spine.001..006" ──
    "shoulderl": "leftshoulder",
    "shoulderr": "rightshoulder",
    # La columna de Mixamo tiene 3 huesos (spine/spine1/spine2); Rigify tiene 6
    # (spine.001..006). Se reparten a los 3 niveles canónicos para que la columna
    # entera siga la animación en vez de quedarse quieta.
    "spine001": "spine",
    "spine002": "spine",
    "spine003": "chest",
    "spine004": "chest",
    "spine005": "upperchest",
    "spine006": "upperchest",
}


def is_armature_object(_self, obj):
    return obj is not None and obj.type == "ARMATURE"


def normalize_bone_name(name: str) -> str:
    value = name.strip().lower()
    # Quitar separadores (espacios, puntos, guiones, guiones bajos) para que
    # "upper_arm.L", "UpperArm L" y "upperarmL" caigan en la misma clave.
    stripped = re.sub(r"[\s._\-]", "", value)
    if stripped in MIXAMO_ALIASES:
        return MIXAMO_ALIASES[stripped]
    if stripped.startswith("mixamorig:"):
        stripped = stripped.replace("mixamorig:", "")
    return GENERIC_ALIASES.get(stripped, stripped)


def build_bone_lookup(armature):
    lookup = {}
    if not armature or armature.type != "ARMATURE":
        return lookup
    for bone in armature.data.bones:
        key = normalize_bone_name(bone.name)
        lookup.setdefault(key, bone.name)
    return lookup


def find_hips_bone(armature):
    """Devuelve el hueso de CADERA/pelvis del armature.

    Muchos rigs (p. ej. el metarig de Rigify) NO tienen un hueso llamado "hips":
    la pelvis es el hueso raíz, a menudo llamado "spine". Si mapeáramos por nombre,
    Mixamo "Hips" no encontraría destino y la pelvis se quedaría sin animar (la cadera
    "se rompe"). Por eso: 1) buscar un nombre explícito de cadera; 2) si no, usar el
    hueso RAÍZ (sin padre), que en la práctica es la pelvis.
    """
    if not armature or armature.type != "ARMATURE":
        return None
    bones = armature.data.bones

    for bone in bones:
        if normalize_bone_name(bone.name) == "hips":
            return bone.name

    roots = [b for b in bones if b.parent is None]
    for bone in roots:
        stripped = re.sub(r"[\s._\-]", "", bone.name.lower())
        if stripped in ("spine", "root", "pelvis", "hips", "cog", "torso", "body"):
            return bone.name
    if roots:
        return roots[0].name
    return None


class GROKO_PG_RetargetBoneMap(PropertyGroup):
    enabled: BoolProperty(name="Enabled", default=True)
    source_bone: StringProperty(name="Mixamo Bone")
    target_bone: StringProperty(name="Target Bone")


class GROKO_PG_RetargetSettings(PropertyGroup):
    source_armature: PointerProperty(
        name="Mixamo Source",
        type=bpy.types.Object,
        poll=is_armature_object,
        description="Armature imported from Mixamo that contains the source animation.",
    )
    target_armature: PointerProperty(
        name="Blender Target",
        type=bpy.types.Object,
        poll=is_armature_object,
        description="Your custom Blender rig that will receive the baked animation.",
    )
    mappings: CollectionProperty(type=GROKO_PG_RetargetBoneMap)
    active_mapping_index: IntProperty(default=0)
    bake_start: IntProperty(name="Start", default=1, min=-100000)
    bake_end: IntProperty(name="End", default=120, min=-100000)
    copy_root_motion: BoolProperty(
        name="Copy Root Motion",
        default=True,
        description="Copy the hips world position (character displacement). "
                    "Turn OFF to animate in place (only rotation), useful for looping cycles.",
    )
    clear_target_action: BoolProperty(
        name="Replace Target Action",
        default=True,
        description="Clear the target action before baking.",
    )


class GROKO_OT_RetargetUseSceneRange(Operator):
    bl_idname = "groko_retarget.use_scene_range"
    bl_label = "Use Scene Range"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        settings = context.scene.groko_retarget_settings
        settings.bake_start = context.scene.frame_start
        settings.bake_end = context.scene.frame_end
        return {"FINISHED"}


class GROKO_OT_RetargetAutoMap(Operator):
    bl_idname = "groko_retarget.auto_map"
    bl_label = "Auto Map Mixamo Bones"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        settings = context.scene.groko_retarget_settings
        source = settings.source_armature
        target = settings.target_armature

        if source is None or target is None:
            self.report({"ERROR"}, "Assign source and target armatures first.")
            return {"CANCELLED"}

        source_lookup = build_bone_lookup(source)
        if not source_lookup:
            self.report({"ERROR"}, "Both armatures need bones.")
            return {"CANCELLED"}

        # canónico -> LISTA de huesos destino con ese nombre, en orden. Necesario porque la
        # columna de Rigify (spine, spine.001, spine.002...) normaliza varios huesos al mismo
        # canónico; hay que poder elegir el SIGUIENTE libre en vez de siempre el primero.
        target_multi = {}
        for bone in target.data.bones:
            target_multi.setdefault(normalize_bone_name(bone.name), []).append(bone.name)
        if not target_multi:
            self.report({"ERROR"}, "Both armatures need bones.")
            return {"CANCELLED"}

        settings.mappings.clear()
        matched = 0
        used_targets = set()

        def add_pair(source_bone, target_bone):
            nonlocal matched
            if not source_bone or not target_bone or target_bone in used_targets:
                return
            item = settings.mappings.add()
            item.enabled = True
            item.source_bone = source_bone
            item.target_bone = target_bone
            used_targets.add(target_bone)
            matched += 1

        def first_free(canonical):
            for name in target_multi.get(canonical, ()):
                if name not in used_targets:
                    return name
            return None

        # CADERA primero, por estructura (hueso raíz), no por nombre: el metarig de Rigify
        # llama "spine" a la pelvis. Se reserva ese hueso para Mixamo "Hips".
        hips_source = source_lookup.get("hips")
        hips_target = find_hips_bone(target)
        add_pair(hips_source, hips_target)

        canonical_order = [
            "spine", "chest", "upperchest", "neck", "head",
            "leftshoulder", "leftupperarm", "leftlowerarm", "lefthand",
            "rightshoulder", "rightupperarm", "rightlowerarm", "righthand",
            "leftupperleg", "leftlowerleg", "leftfoot", "lefttoe",
            "rightupperleg", "rightlowerleg", "rightfoot", "righttoe",
        ]

        # Cada canónico coge el SIGUIENTE hueso libre con ese nombre. Así "spine"(pelvis) va a
        # Hips y la primera vértebra "spine.001" queda libre para Mixamo "Spine".
        for canonical in canonical_order:
            add_pair(source_lookup.get(canonical), first_free(canonical))

        # Extra fallback: mismos nombres normalizados no cubiertos arriba (dedos, etc.).
        for key, source_bone in source_lookup.items():
            add_pair(source_bone, first_free(key))

        if matched == 0:
            self.report({"WARNING"}, "No matching bones found. Add mappings manually.")
        else:
            self.report({"INFO"}, f"Mapped {matched} bones.")
        return {"FINISHED"}


class GROKO_OT_RetargetAddMapping(Operator):
    bl_idname = "groko_retarget.add_mapping"
    bl_label = "Add Bone Mapping"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        settings = context.scene.groko_retarget_settings
        item = settings.mappings.add()
        item.enabled = True
        settings.active_mapping_index = len(settings.mappings) - 1
        return {"FINISHED"}


class GROKO_OT_RetargetRemoveMapping(Operator):
    bl_idname = "groko_retarget.remove_mapping"
    bl_label = "Remove Bone Mapping"
    bl_options = {"REGISTER", "UNDO"}

    index: IntProperty(default=-1)

    def execute(self, context):
        settings = context.scene.groko_retarget_settings
        idx = self.index if self.index >= 0 else settings.active_mapping_index
        if 0 <= idx < len(settings.mappings):
            settings.mappings.remove(idx)
            settings.active_mapping_index = min(idx, max(0, len(settings.mappings) - 1))
        return {"FINISHED"}


class GROKO_OT_RetargetBake(Operator):
    bl_idname = "groko_retarget.bake"
    bl_label = "Bake to Target Rig"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        settings = context.scene.groko_retarget_settings
        source = settings.source_armature
        target = settings.target_armature

        if source is None or target is None:
            self.report({"ERROR"}, "Assign source and target armatures first.")
            return {"CANCELLED"}
        if source == target:
            self.report({"ERROR"}, "Source and target cannot be the same armature.")
            return {"CANCELLED"}
        if settings.bake_end < settings.bake_start:
            self.report({"ERROR"}, "Bake end must be greater than or equal to start.")
            return {"CANCELLED"}

        mappings = [
            item for item in settings.mappings
            if item.enabled and item.source_bone in source.pose.bones and item.target_bone in target.pose.bones
        ]
        if not mappings:
            self.report({"ERROR"}, "No valid bone mappings.")
            return {"CANCELLED"}

        previous_frame = context.scene.frame_current
        start = settings.bake_start
        end = settings.bake_end
        root_motion = settings.copy_root_motion

        try:
            # ── Precálculo por par: offset de REPOSO en espacio de hueso ──────────────
            # Retargeting correcto entre rigs de proporciones/orientaciones distintas:
            # se transfiere la ROTACIÓN LOCAL de cada hueso (su movimiento respecto a SU
            # reposo), no la orientación mundial. Se "conjuga" por la diferencia de reposo
            # C = (reposo_destino_mundo)⁻¹ · (reposo_origen_mundo), de modo que
            #   rot_local_destino = C · rot_local_origen · C⁻¹.
            # Si ambos reposos coinciden (torso), C = identidad → copia directa. Si difieren
            # (piernas digitígradas del lobo vs humano de Mixamo), cada pierna se dobla desde
            # SU reposo y ya no se retuerce ni se rasga.
            pairs = []
            for item in mappings:
                tgt_pb = target.pose.bones[item.target_bone]
                src_pb = source.pose.bones[item.source_bone]
                src_rest = (source.matrix_world @ src_pb.bone.matrix_local).to_quaternion()
                tgt_rest = (target.matrix_world @ tgt_pb.bone.matrix_local).to_quaternion()
                conj = tgt_rest.inverted() @ src_rest
                is_hips = normalize_bone_name(item.source_bone) == "hips"
                # Para la cadera (hueso raíz) se coloca la cabeza en la posición mundial de
                # Mixamo → root motion. P es constante porque el hueso no tiene padre.
                hips_p = (target.matrix_world @ tgt_pb.bone.matrix_local) if is_hips else None
                pairs.append((tgt_pb, src_pb, conj, is_hips, hips_p))

            # Acción nueva y limpia en el destino.
            target.animation_data_create()
            if settings.clear_target_action and target.animation_data.action:
                target.animation_data.action = None
            target.animation_data.action = bpy.data.actions.new(
                name=f"{target.name}_Mixamo_Retarget"
            )

            # ── Bake: por cada frame, leer pose de Mixamo y escribir la del destino ──
            for frame in range(start, end + 1):
                context.scene.frame_set(frame)
                for (tgt_pb, src_pb, conj, is_hips, hips_p) in pairs:
                    q_src = src_pb.matrix_basis.to_quaternion()   # rotación local de Mixamo
                    q_tgt = conj @ q_src @ conj.inverted()         # llevada al hueso destino

                    if tgt_pb.rotation_mode == "QUATERNION":
                        tgt_pb.rotation_quaternion = q_tgt
                        tgt_pb.keyframe_insert("rotation_quaternion", frame=frame)
                    else:
                        tgt_pb.rotation_euler = q_tgt.to_euler(tgt_pb.rotation_mode)
                        tgt_pb.keyframe_insert("rotation_euler", frame=frame)

                    if is_hips and root_motion and hips_p is not None:
                        world_head = (source.matrix_world @ src_pb.matrix).to_translation()
                        loc = hips_p.to_3x3().inverted() @ (world_head - hips_p.to_translation())
                        tgt_pb.location = loc
                        tgt_pb.keyframe_insert("location", frame=frame)

            self.report({"INFO"}, f"Baked {len(pairs)} bones ({start}-{end}). Rest-offset retarget.")
            return {"FINISHED"}

        except Exception as ex:
            self.report({"ERROR"}, f"Retarget bake failed: {ex}")
            return {"CANCELLED"}

        finally:
            context.scene.frame_set(previous_frame)


class GROKO_PT_MixamoRetargeter(Panel):
    bl_label = "Mixamo Retargeter"
    bl_idname = "GROKO_PT_mixamo_retargeter"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Groko"

    def draw(self, context):
        layout = self.layout
        settings = context.scene.groko_retarget_settings

        layout.label(text="GrokoEngine Animation Tools", icon="ARMATURE_DATA")
        box = layout.box()
        box.prop(settings, "source_armature")
        box.prop(settings, "target_armature")

        row = box.row(align=True)
        row.operator("groko_retarget.auto_map", icon="CONSTRAINT_BONE")
        row.operator("groko_retarget.add_mapping", text="", icon="ADD")

        range_box = layout.box()
        range_box.label(text="Bake Range", icon="TIME")
        row = range_box.row(align=True)
        row.prop(settings, "bake_start")
        row.prop(settings, "bake_end")
        range_box.operator("groko_retarget.use_scene_range", icon="PREVIEW_RANGE")
        range_box.prop(settings, "clear_target_action")
        range_box.prop(settings, "copy_root_motion")

        map_box = layout.box()
        row = map_box.row()
        row.label(text=f"Bone Mapping ({len(settings.mappings)})", icon="BONE_DATA")
        if settings.mappings:
            row.operator("groko_retarget.remove_mapping", text="", icon="REMOVE")

        source = settings.source_armature
        target = settings.target_armature
        for index, item in enumerate(settings.mappings):
            row = map_box.row(align=True)
            row.prop(item, "enabled", text="")

            col = row.column(align=True)
            if source:
                col.prop_search(item, "source_bone", source.data, "bones", text="")
            else:
                col.prop(item, "source_bone", text="")

            col = row.column(align=True)
            if target:
                col.prop_search(item, "target_bone", target.data, "bones", text="")
            else:
                col.prop(item, "target_bone", text="")

            remove = row.operator("groko_retarget.remove_mapping", text="", icon="X")
            remove.index = index

        layout.separator()
        layout.operator("groko_retarget.bake", icon="ACTION", text="Bake Animation to Target Rig")

        help_box = layout.box()
        help_box.label(text="Workflow", icon="INFO")
        help_box.label(text="1. Import Mixamo FBX with animation.")
        help_box.label(text="2. Select Mixamo Source and Blender Target.")
        help_box.label(text="3. Auto Map, fix missing bones if needed.")
        help_box.label(text="4. Bake. Export target rig to GrokoEngine.")


classes = (
    GROKO_PG_RetargetBoneMap,
    GROKO_PG_RetargetSettings,
    GROKO_OT_RetargetUseSceneRange,
    GROKO_OT_RetargetAutoMap,
    GROKO_OT_RetargetAddMapping,
    GROKO_OT_RetargetRemoveMapping,
    GROKO_OT_RetargetBake,
    GROKO_PT_MixamoRetargeter,
)


def register():
    for cls in classes:
        bpy.utils.register_class(cls)
    bpy.types.Scene.groko_retarget_settings = PointerProperty(type=GROKO_PG_RetargetSettings)


def unregister():
    del bpy.types.Scene.groko_retarget_settings
    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
