# generate_photo.py
from PIL import Image, ImageDraw, ImageFont

def create_image(path='generated_photo.png'):
    width, height = 400, 300
    background_color = (73, 109, 137)  # blueish
    text_color = (255, 255, 255)      # white
    image = Image.new('RGB', (width, height), color=background_color)
    draw = ImageDraw.Draw(image)
    try:
        font = ImageFont.truetype("arial.ttf", 40)
    except IOError:
        font = ImageFont.load_default()
    text = "Hola Mundo"
    text_width, text_height = draw.textsize(text, font=font)
    text_position = ((width - text_width) / 2, (height - text_height) / 2)
    draw.text(text_position, text, fill=text_color, font=font)
    image.save(path)
    print(f"Imagen guardada en {path}")

if __name__ == "__main__":
    create_image()
