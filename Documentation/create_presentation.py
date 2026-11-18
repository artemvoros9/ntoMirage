from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN

prs=Presentation(); prs.slide_width=Inches(13.333); prs.slide_height=Inches(7.5)
slides=[
('RADIATION SANDBOX','Интерактивная 3D-песочница распространения радиации\nКомандная задача ТВР • техническая презентация'),
('Задача и игровой цикл','Источник → поток частиц → столкновения → дозиметры\n\nИгрок кликает по объектам, перетаскивает их и меняет параметры без остановки симуляции.'),
('Физическое ядро','Каждая частица — отдельный физический объект\n• сферический поток / конус / луч\n• индивидуальный lifetime с вариативностью\n• закон обратных квадратов\n• SphereCast столкновения\n• магнитное искривление траекторий'),
('Механики взаимодействия','Глухая преграда: уничтожение частицы\nНастраиваемая преграда: T = exp(-μρL)\nЗеркало: отражение вектора скорости\nЗамедлитель: trigger-зона, потеря скорости и интенсивности\nКритический объект: вторичный omni-источник'),
('Дозиметры и визуальная обратная связь','Настраиваемая сетка дозиметров\nПлавный градиент: синий → cyan → жёлтый → красный\nКритическая доза переводит объект в новый источник\nGPU instancing снижает стоимость отображения частиц'),
('Debug-панель и управление','FPS, активные частицы, коллайдеры, CPU-время кадра\nПауза, скорость 0.1×–3×, перезапуск\nПереключение сценариев\nВыбор, outline, drag, live-параметры источника/преград'),
('Три демонстрационных сценария','01 Concrete attenuation — конус и блоки разной толщины\n02 Mirror delivery — отражение в коробку + магнит, три магнита, активен один\n03 Secondary emission — графитовый стержень + газовый замедлитель'),
('Архитектура проекта','RadiationSource\n  └─ RadiationParticle (custom kinematics)\n      ├─ IRadiationInteraction\n      ├─ IRadiationVolume\n      └─ RadiationMagnet\nRadiationDosimeterField / CriticalRadiationObject\nRadiationSandboxController — UI и интеракция'),
('Итог','Песочница превращает формулы и столкновения в наблюдаемую систему.\n\nСледующий шаг: профилирование больших потоков и расширение набора материалов.')]
for idx,(title,body) in enumerate(slides):
 s=prs.slides.add_slide(prs.slide_layouts[6]); bg=s.background.fill; bg.solid(); bg.fore_color.rgb=RGBColor(7,16,27)
 t=s.shapes.add_textbox(Inches(.75),Inches(.65),Inches(11.8),Inches(1.1)); tf=t.text_frame; p=tf.paragraphs[0]; p.text=title; p.font.size=Pt(30); p.font.bold=True; p.font.color.rgb=RGBColor(60,230,255)
 b=s.shapes.add_textbox(Inches(.9),Inches(2.0),Inches(11.3),Inches(4.5)); tf=b.text_frame; tf.word_wrap=True
 for j,line in enumerate(body.split('\n')):
  p=tf.paragraphs[0] if j==0 else tf.add_paragraph(); p.text=line; p.font.size=Pt(21 if j==0 else 18); p.font.color.rgb=RGBColor(235,245,250); p.space_after=Pt(10)
 f=s.shapes.add_textbox(Inches(.9),Inches(7.05),Inches(11),Inches(.25)); f.text_frame.paragraphs[0].text=f'{idx+1:02d}  /  TVR RADIATION LAB'; f.text_frame.paragraphs[0].font.size=Pt(9); f.text_frame.paragraphs[0].font.color.rgb=RGBColor(100,150,165)
prs.save('Documentation/Radiation_Sandbox_Presentation.pptx')
