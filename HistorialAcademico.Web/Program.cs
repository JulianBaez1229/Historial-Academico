using HistorialAcademico.Banner;
using HistorialAcademico.Core.Perfiles;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Helpers;
using HistorialAcademico.Web.Perfiles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using HistorialAcademico.Web.Services;
using Microsoft.EntityFrameworkCore;

// El programa descargado puede abrirse desde un acceso directo con otra carpeta de trabajo: se vuelve a la suya (wwwroot, pensums).
Arranque.AjustarCarpetaDeTrabajo(Environment.ProcessPath, AppContext.BaseDirectory, Directory.SetCurrentDirectory);

var builder = WebApplication.CreateBuilder(args);

// Solo el programa publicado (appsettings.Production.json) enciende estas tres cosas; en desarrollo y en las pruebas están apagadas.
var inicio = builder.Configuration.GetSection("Inicio");
var puertoLibre = inicio.GetValue<bool>("PuertoLibre");
if (puertoLibre && string.IsNullOrEmpty(builder.Configuration["urls"]))
    builder.WebHost.UseUrls($"http://localhost:{Arranque.PuertoLibre(inicio.GetValue("Puerto", Arranque.PuertoPorOmision))}");

// Add services to the container.
builder.Services.AddControllersWithViews();

// La vista previa de un pénsum importado envía hasta ~300 filas de varios campos: más que el límite por omisión (1024 valores).
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.ValueCountLimit = 4000);

// Perfiles: cada persona que usa la aplicación en este equipo tiene su propia base de datos y su propia sesión de Banner, en una
// carpeta del usuario (%LOCALAPPDATA%\HistorialAcademico, o «Perfiles:Carpeta») y nunca dentro del repositorio.
// Las claves con que se protege la cookie de perfil viven en esa misma carpeta, así sobreviven a los reinicios.
builder.Services.AddDataProtection()
    .SetApplicationName("HistorialAcademico")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(GestorPerfiles.CarpetaDatosUsuario(builder.Configuration), "claves")));
builder.Services.AddSingleton<GestorPerfiles>();
builder.Services.AddScoped<PerfilActual>();

// Banner:BaseUrl viene de user-secrets. Nunca se guardan usuario ni contraseña. La carpeta de la sesión es la del perfil.
// La dirección de Banner sale de la universidad que el perfil eligió (si aún no eligió ninguna, de la configuración).
builder.Services.AddScoped(sp =>
{
    var perfil = sp.GetRequiredService<PerfilActual>();
    var urlUniversidad = perfil.UniversidadId is null ? null : sp.GetRequiredService<ReglasUniversidadService>().Activa.UrlBanner;
    return sp.GetRequiredService<GestorPerfiles>().OpcionesBanner(perfil, urlUniversidad);
});
builder.Services.AddScoped<BannerClient>();

// SQLite local: la base del perfil en uso.
// Las herramientas de EF (dotnet ef migrations add…) arman la aplicación sin ningún perfil: solo ahí se admite una base de mentira,
// que nunca se abre. En cualquier otro caso, abrir una base sin perfil es un error.
var herramientaDeEf = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name is "ef" or "dotnet-ef";
builder.Services.AddDbContext<HistorialContext>((sp, o) =>
{
    var perfil = sp.GetRequiredService<PerfilActual>();
    o.UseSqlite(herramientaDeEf && !perfil.Hay ? "Data Source=diseno-no-se-usa.db" : perfil.CadenaConexion);
});
builder.Services.AddScoped<SincronizacionService>();
builder.Services.AddScoped<ReglasUniversidadService>();
builder.Services.AddScoped<AcademicoService>();
builder.Services.AddScoped<PlanificadorService>();
builder.Services.AddScoped<HorariosService>();
builder.Services.AddSingleton<ConsultaMasivaService>();
builder.Services.AddScoped<CarreraService>();
builder.Services.AddScoped<MateriasManualesService>();
builder.Services.AddScoped<ExportadorDatosService>();
builder.Services.AddScoped<VistaATexto>();
builder.Services.AddSingleton<ExportadorPlanService>();
builder.Services.AddScoped<HorarioTentativoService>();
builder.Services.AddScoped<AperturaService>();

// Avisos de versiones nuevas: preferencias del equipo (fuera de los perfiles) y la consulta a GitHub, que solo existe si «Actualizaciones:Activas».
builder.Services.AddSingleton(sp => new AlmacenPreferencias(sp.GetRequiredService<GestorPerfiles>().Almacen.Raiz));
builder.Services.AddSingleton(sp => new ActualizacionesService(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<AlmacenPreferencias>()));

var app = builder.Build();

// Interfaz en español dominicano: fechas y decimales iguales en cualquier equipo.
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(Ui.Cultura),
    SupportedCultures = new[] { Ui.Cultura },
    SupportedUICultures = new[] { Ui.Cultura },
});

// Con una base fija («Perfiles:BaseFija», pruebas) se crea y migra al arrancar; con perfiles, cada base se migra al entrar a su perfil.
if (app.Services.GetRequiredService<GestorPerfiles>().EsFijo)
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<GestorPerfiles>()
        .AsegurarBase(scope.ServiceProvider.GetRequiredService<PerfilActual>(), scope.ServiceProvider.GetRequiredService<HistorialContext>());
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (!puertoLibre) app.UseHttpsRedirection();   // el programa descargado solo escucha en http://localhost: no hay https al que redirigir
app.UseStaticFiles();

app.UseMiddleware<PerfilMiddleware>();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Primer arranque del programa descargado: instala el navegador si falta (mostrando el avance) y abre la aplicación en el navegador.
if (inicio.GetValue<bool>("InstalarNavegador")) new InstaladorNavegador().AsegurarChromium(Console.Out);
if (inicio.GetValue<bool>("AbrirNavegador"))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var url = Arranque.UrlParaAbrir(app.Urls);
        if (url is null) return;
        Console.WriteLine($"Historial Académico está listo en {url}");
        Console.WriteLine("Deja esta ventana abierta mientras lo usas; ciérrala para salir.");
        if (!Arranque.AbrirEnNavegador(url)) Console.WriteLine("No pude abrir el navegador solo: copia la dirección de arriba en tu navegador.");
    });
}

// Al abrirse, una sola consulta (si la persona no la apagó y este es un programa publicado). Sin esperar: la aplicación no se atrasa por eso.
app.Lifetime.ApplicationStarted.Register(() => _ = app.Services.GetRequiredService<ActualizacionesService>().ConsultarAsync());

app.Run();

// Permite que las pruebas de integración arranquen la aplicación completa.
public partial class Program { }
