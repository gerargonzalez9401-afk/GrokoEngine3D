from reportlab.lib import colors
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import inch
from reportlab.platypus import Paragraph, SimpleDocTemplate, Spacer, Table, TableStyle


OUT = "Docs/GrokoEngine_Atajos_Teclado.pdf"


def p(text, style):
    return Paragraph(text, style)


def shortcut_table(title, rows, styles):
    data = [
        [
            p("<b>Atajo</b>", styles["TableHeader"]),
            p("<b>Accion</b>", styles["TableHeader"]),
            p("<b>Notas</b>", styles["TableHeader"]),
        ]
    ]
    for shortcut, action, notes in rows:
        data.append([p(shortcut, styles["Cell"]), p(action, styles["Cell"]), p(notes, styles["Cell"])])

    table = Table(data, colWidths=[1.35 * inch, 1.75 * inch, 3.05 * inch], hAlign="LEFT")
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#E8EEF5")),
                ("TEXTCOLOR", (0, 0), (-1, 0), colors.HexColor("#1F4D78")),
                ("GRID", (0, 0), (-1, -1), 0.35, colors.HexColor("#B8C4D0")),
                ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
                ("TOPPADDING", (0, 0), (-1, -1), 5),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
                ("LEFTPADDING", (0, 0), (-1, -1), 6),
                ("RIGHTPADDING", (0, 0), (-1, -1), 6),
            ]
        )
    )
    return [p(title, styles["H2"]), table, Spacer(1, 0.14 * inch)]


def main():
    doc = SimpleDocTemplate(
        OUT,
        pagesize=letter,
        rightMargin=0.55 * inch,
        leftMargin=0.55 * inch,
        topMargin=0.5 * inch,
        bottomMargin=0.45 * inch,
        title="GrokoEngine - Atajos de teclado",
    )

    styles = getSampleStyleSheet()
    styles.add(
        ParagraphStyle(
            "TitleBlue",
            parent=styles["Title"],
            fontName="Helvetica-Bold",
            fontSize=18,
            leading=22,
            textColor=colors.HexColor("#2E74B5"),
            alignment=1,
            spaceAfter=4,
        )
    )
    styles.add(
        ParagraphStyle(
            "SubtitleSmall",
            parent=styles["Normal"],
            fontName="Helvetica",
            fontSize=9.5,
            leading=12,
            textColor=colors.HexColor("#666666"),
            alignment=1,
            spaceAfter=9,
        )
    )
    styles.add(
        ParagraphStyle(
            "H2",
            parent=styles["Heading2"],
            fontName="Helvetica-Bold",
            fontSize=11.5,
            leading=14,
            textColor=colors.HexColor("#2E74B5"),
            spaceBefore=8,
            spaceAfter=5,
        )
    )
    styles.add(
        ParagraphStyle(
            "Cell",
            parent=styles["Normal"],
            fontName="Helvetica",
            fontSize=8.5,
            leading=10.5,
            textColor=colors.HexColor("#222222"),
        )
    )
    styles.add(
        ParagraphStyle(
            "TableHeader",
            parent=styles["Cell"],
            fontName="Helvetica-Bold",
            textColor=colors.HexColor("#1F4D78"),
        )
    )
    styles.add(
        ParagraphStyle(
            "Note",
            parent=styles["Normal"],
            fontName="Helvetica",
            fontSize=8.8,
            leading=11,
            leftIndent=10,
            bulletIndent=0,
            spaceAfter=2,
        )
    )

    story = [
        p("GrokoEngine - Atajos de teclado y controles", styles["TitleBlue"]),
        p("Guia rapida del editor", styles["SubtitleSmall"]),
    ]

    sections = [
        (
            "Teclado",
            [
                ("Ctrl + Z", "Deshacer", "Restaura el estado anterior de la escena."),
                ("Ctrl + Y", "Rehacer", "Vuelve al estado siguiente despues de deshacer."),
                ("Ctrl + D", "Duplicar seleccion", "Duplica objetos seleccionados con hijos y componentes."),
                ("Delete / Supr", "Borrar seleccion", "Elimina el objeto o seleccion actual."),
                ("F", "Enfocar objeto", "Mueve la camara hacia el objeto seleccionado."),
                ("W A S D", "Mover camara", "Funciona mientras mantienes click derecho en el viewport."),
                ("Space", "Input de scripts", "Disponible en scripts con Input.GetKeyDown(KeyCode.Space)."),
            ],
        ),
        (
            "Mouse en viewport",
            [
                ("Click izquierdo", "Seleccionar objeto", "Usa raycast contra el volumen del objeto."),
                ("Shift + click izquierdo", "Multiseleccion", "Agrega o quita objetos de la seleccion."),
                ("Arrastrar en vacio", "Marco de seleccion", "Selecciona objetos dentro del rectangulo."),
                ("Click derecho + mover mouse", "Mirar con camara", "Rota la vista de la camara del editor."),
                ("Click derecho + W A S D", "Navegar escena", "Movimiento libre por el viewport."),
                ("Arrastrar eje de gizmo", "Mover, rotar o escalar", "Respeta Snap si Snap esta activo."),
            ],
        ),
        (
            "Toolbar y modos",
            [
                ("Mover", "Gizmo de posicion", "Boton de herramienta en la barra superior."),
                ("Rotar", "Gizmo de rotacion", "Boton de herramienta en la barra superior."),
                ("Escalar", "Gizmo de escala", "Boton de herramienta en la barra superior."),
                ("Snap: ON/OFF", "Activar snap", "Afecta mover, rotar y escalar con gizmo."),
                ("VSync: ON/OFF", "Limitar FPS por VSync", "Alterna sincronizacion vertical."),
                ("Guardar Proyecto", "Guardar escena", "Guarda en Assets/Scenes/Main.gscene."),
                ("Apply Prefab", "Guardar cambios al prefab", "Disponible si la instancia viene de un prefab."),
            ],
        ),
        (
            "Prefabs y Project",
            [
                ("Arrastrar objeto a Project", "Crear prefab", "Crea un archivo .prefab desde la jerarquia."),
                ("Doble click en prefab", "Instanciar prefab", "Agrega una instancia a la escena."),
                ("Click en prefab", "Editar prefab en Inspector", "Permite ver y modificar sus componentes."),
                ("Arrastrar prefab a jerarquia", "Instanciar como hijo", "Lo coloca dentro del objeto destino."),
                ("Arrastrar prefab a viewport", "Soltar en escena", "Detecta suelo/colliders y lo coloca encima."),
                ("Arrastrar script a objeto", "Agregar script", "Suelta un .cs sobre un objeto de la jerarquia."),
            ],
        ),
    ]

    for title, rows in sections:
        story.extend(shortcut_table(title, rows, styles))

    story.append(p("Notas rapidas", styles["H2"]))
    notes = [
        "Los cubos seleccionados muestran wireframe amarillo en el viewport.",
        "El preview azul de prefab indica donde se soltara el objeto.",
        "Undo/Redo trabaja con snapshots de escena y no modifica archivos hasta guardar.",
        "En Play Mode no se deben editar componentes desde el Inspector.",
    ]
    for item in notes:
        story.append(p(f"- {item}", styles["Note"]))

    doc.build(story)


if __name__ == "__main__":
    main()
