# Requisitos de FitLab

Enunciado original con el que se construyó la primera versión, y cómo quedó cada punto en la app actual.

## Enunciado original

FitLab es un probador virtual de ropa con guardarropa digital y guía de colores. Interfaz en español,
diseño limpio y moderno, pensado primero para celular (mobile-first) pero usable en escritorio.

### Usuarios
- Registro e inicio de sesión con correo y contraseña.
- Cada usuario ve solo su propio guardarropa y sus propios outfits.

### Módulo 1 — Guardarropa digital
- El usuario sube prendas con foto (desde la galería o la cámara).
- Campos por prenda: nombre, categoría (parte superior, parte inferior, calzado, abrigo, accesorio),
  color principal (selector de color + nombre), temporada (todo el año, calor, frío) y notas opcionales.
- Vista en cuadrícula con filtros por categoría, color y temporada, y buscador por nombre.
- Editar y eliminar prendas.

### Módulo 2 — Probador virtual
- Pantalla con un maniquí y "ranuras" por categoría: parte superior, parte inferior, calzado, abrigo, accesorio.
- El usuario toca una ranura y elige una prenda de su guardarropa para ver la combinación completa.
- Botón "Aleatorio" que arma una combinación con una prenda de cada categoría obligatoria
  (superior, inferior, calzado).
- Guardar la combinación como "Outfit" con nombre y ocasión (casual, universidad, trabajo, salida, deporte).
- Lista de outfits guardados, con favoritos y opción de eliminarlos.

### Módulo 3 — Guía de colores
- Al armar un outfit, indicador de armonía de colores según los colores principales de las prendas:
  complementarios, análogos, monocromáticos o neutros, con un texto corto que explica por qué
  combinan o no y una sugerencia (por ejemplo: "cambiá el calzado por uno neutro").
- Sección "Guía" con una rueda de color interactiva: el usuario elige un color y la app muestra qué
  colores combinan y qué prendas de su propio guardarropa encajan.

### Datos
- Base de datos con las tablas `usuarios`, `prendas`, `outfits` y la intermedia `outfit_prendas`.
- Las fotos se guardan como archivos, no dentro de la base de datos.

### Reglas
- Primero el MVP completo con los tres módulos; nada de pagos, redes sociales ni funciones extra.
- Validar formularios (campos obligatorios, tamaño máximo de imagen).
- Estados vacíos claros (por ejemplo: "Todavía no tenés prendas, subí la primera").
- Mensajes de error en español y amigables.

## Diferencias con el enunciado

| Punto | Enunciado | Cómo quedó |
|---|---|---|
| Plataforma | Aplicación web | App de escritorio para Windows (.NET 10 + Windows Forms), con diseño adaptable de celular a escritorio. |
| Cámara | Cámara del celular | Abre la app Cámara de Windows y después se elige la foto desde su carpeta. |
| Maniquí | Silueta simple con ranuras que muestran la foto | Maniquí con volumen que se viste con las prendas: se les quita el fondo y se ponen en capas sobre el cuerpo. |
| Guardar outfit | — | Exige las tres categorías obligatorias (superior, inferior y calzado). |
| Formatos de foto | — | JPG, PNG y WebP (también HEIC y AVIF si Windows tiene esas extensiones); máximo 5 MB. |
| Contraseñas | — | Agregado: código de recuperación por si se olvida la contraseña. |
| Accesibilidad | — | Agregado: uso completo con teclado y nombres para lectores de pantalla. |
