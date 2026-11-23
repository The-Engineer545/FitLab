// Db y Photos guardan su configuración en variables estáticas (una sola carpeta de datos a la vez),
// así que las pruebas se corren de a una para que no se pisen entre sí.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
