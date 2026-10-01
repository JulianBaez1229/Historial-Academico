using HistorialAcademico.Core.Perfiles;
using HistorialAcademico.Web.Data;

namespace HistorialAcademico.Web.Perfiles;

/// <summary>
/// Antes de cualquier pantalla se sabe con qué perfil se trabaja: si la cookie lo dice, se usa; si no, se lleva a crear el primer
/// perfil, se entra directo al único perfil sin PIN o se pide elegir (y poner el PIN). Sin perfil no se abre ninguna base de datos.
/// </summary>
public sealed class PerfilMiddleware
{
    private readonly RequestDelegate _siguiente;
    private readonly GestorPerfiles _gestor;

    public PerfilMiddleware(RequestDelegate siguiente, GestorPerfiles gestor)
    {
        _siguiente = siguiente;
        _gestor = gestor;
    }

    /// <summary>Lo que se puede abrir sin haber elegido perfil: elegir o crear uno y la bienvenida del asistente.</summary>
    private static bool Exenta(PathString ruta) =>
        ruta.StartsWithSegments("/Perfiles", StringComparison.OrdinalIgnoreCase) ||
        ruta.StartsWithSegments("/Asistente/Bienvenida", StringComparison.OrdinalIgnoreCase) ||
        ruta.StartsWithSegments("/Home/Error", StringComparison.OrdinalIgnoreCase) ||
        ruta.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext contexto, PerfilActual actual)
    {
        if (_gestor.EsFijo || Exenta(contexto.Request.Path)) { await _siguiente(contexto); return; }

        var perfil = _gestor.Almacen.Obtener(_gestor.LeerCookie(contexto.Request));
        if (perfil is null)
        {
            var todos = _gestor.Almacen.Listar();
            if (todos.Count == 0) { contexto.Response.Redirect("/Asistente/Bienvenida"); return; }   // primer uso: empieza el asistente
            if (todos.Count == 1 && !todos[0].TienePin)
            {
                perfil = todos[0];
                _gestor.EmitirCookie(contexto, perfil.Id);
            }
            else { contexto.Response.Redirect("/Perfiles"); return; }
        }

        actual.Establecer(perfil);

        // A medias con el asistente de primer uso: se sigue donde quedó (el asistente siempre deja omitir lo que falte).
        if (perfil.PasoAsistente != PasosAsistente.Terminado && !contexto.Request.Path.StartsWithSegments("/Asistente", StringComparison.OrdinalIgnoreCase))
        {
            contexto.Response.Redirect("/Asistente");
            return;
        }

        _gestor.AsegurarBase(actual, contexto.RequestServices.GetRequiredService<HistorialContext>());
        await _siguiente(contexto);
    }
}
