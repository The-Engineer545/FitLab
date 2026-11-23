# FitLab — tecnologías usadas

FitLab es una aplicación de escritorio para Windows, en español.

## Frontend (carpeta `frontend/`)
- **C# con .NET 10** — lenguaje y plataforma.
- **Windows Forms** — ventanas y pantallas, con controles propios dibujados a mano
  (botones, tarjetas, maniquí del probador, rueda de color) usando **GDI+ (System.Drawing)**.
- Diseño pensado primero para pantallas angostas (tipo celular) que se adapta a escritorio.

## Backend (carpeta `backend/`)
- **C# con .NET 10** (biblioteca separada, `FitLab.Backend`).
- **SQLite** como base de datos, a través de **Microsoft.Data.Sqlite**.
  Tablas: `usuarios`, `prendas`, `outfits` y `outfit_prendas`.
- **PBKDF2 con SHA-256** (incluido en .NET, 600.000 vueltas) para guardar las contraseñas.
  Las cuentas creadas con la protección anterior se actualizan solas al iniciar sesión.
- El esquema de la base lleva número de versión (`PRAGMA user_version`); los cambios futuros
  se agregan en `Db.Migrate`.
- **GDI+ (System.Drawing)** para validar, girar y achicar las fotos (máximo 5 MB). Si GDI+ no
  entiende el archivo, se usa el **lector de imágenes de Windows (WIC, vía WPF)**, que abre WebP
  —el formato de muchas fotos descargadas de tiendas, aunque se llamen `.jpg`— y también HEIC o
  AVIF si están instaladas esas extensiones de Windows. Las fotos se guardan como archivos JPG
  en una carpeta, no dentro de la base de datos.
- Análisis de armonía de colores propio (tonos HSV y espacio de color CIELAB).
- **Probador:** maniquí dibujado con volumen (degradés y sombras de GDI+). A cada foto se le quita
  el fondo automáticamente (`Photos.MakeCutout`: parte de los bordes y sigue el color del fondo,
  incluso con degradé) y la prenda se coloca sobre su zona del cuerpo, en capas. Si el fondo de la
  foto no es parejo, se muestra entera. Es una vista 2D: el maniquí no gira.

## Dónde se guardan los datos
`%LOCALAPPDATA%\FitLab\` → `fitlab.db` (base de datos) y `fotos\` (imágenes de las prendas).
La variable de entorno `FITLAB_DATA` permite usar otra carpeta.

Los datos no están cifrados: las cuentas separan los guardarropas dentro de la app, pero quien
use la misma sesión de Windows puede abrir esos archivos.

## Recuperación de contraseña
Al crear la cuenta, FitLab muestra **una sola vez** un código de recuperación (`XXXX-XXXX-XXXX`).
Si la persona olvida la contraseña, en el inicio toca «¿Olvidaste tu contraseña?», escribe su correo,
el código y una contraseña nueva. Cada código sirve una vez: al usarlo se muestra uno nuevo.
Del código solo se guarda el hash (PBKDF2, igual que la contraseña). Las cuentas creadas antes de
esta función reciben su código la próxima vez que inician sesión.

## Cómo compilar y ejecutar
- `dotnet build` dentro de esta carpeta.
- `dotnet run` para abrir la app.

## Pruebas (carpeta `tests/`)
`dotnet test tests` corre las pruebas automáticas del backend (xUnit): armonía de colores,
cuentas, recuperación de contraseña, migración de bases viejas, prendas, outfits y fotos. Usan una carpeta temporal, no tocan tus datos.

## Teclado
- **Tab** recorre botones, tarjetas y campos; **Enter** o **Espacio** los activan; **Esc** cierra los diálogos.
- Barra inferior: flechas izquierda/derecha cambian de pantalla.
- Maniquí: flechas para moverse entre las prendas, Enter para elegir una, Suprimir para quitarla.
- Rueda de color: izquierda/derecha cambian el tono; arriba/abajo, la intensidad.
- Tarjeta de outfit: Enter lo abre, F lo marca favorito, Suprimir lo elimina.

## Solo en compilaciones de depuración
`--screenshot <archivo>` y las variables `FITLAB_DEMO`, `FITLAB_TAB` y `FITLAB_W` sirven para
sacar capturas automáticas con una cuenta de ejemplo. No existen en la versión Release.
