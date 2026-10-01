// Avance de la consulta de varias materias a Banner: pregunta cada par de segundos y actualiza la barra.
// Sin JavaScript la pantalla sigue funcionando (muestra el estado al recargarla).
(function () {
    'use strict';
    var panel = document.getElementById('panel-progreso');
    if (!panel || panel.getAttribute('data-activa') !== 'true') return;

    var url = panel.getAttribute('data-url');
    var barra = document.getElementById('barra-progreso');
    var fase = document.getElementById('progreso-fase');
    var actual = document.getElementById('progreso-actual');
    var cuenta = document.getElementById('progreso-cuenta');
    var lista = document.getElementById('progreso-lista');
    var etiquetas = { ConSecciones: 'con secciones', SinSecciones: 'sin secciones publicadas', Error: 'con error', Omitida: 'omitida' };
    var fallos = 0;

    function pintar(d) {
        var pct = d.porcentaje;
        barra.style.width = pct + '%';
        barra.setAttribute('aria-valuenow', pct);
        barra.textContent = pct + '%';
        fase.textContent = d.fase;
        actual.textContent = d.actual ? 'Consultando ahora: ' + d.actual : '';
        cuenta.textContent = d.hechas + ' de ' + d.total + ' materias';
        lista.textContent = '';
        d.items.forEach(function (i) {
            var li = document.createElement('li');
            li.textContent = i.codigo + ' · ' + i.nombre + ' — ' + (etiquetas[i.resultado] || i.resultado) +
                (i.resultado === 'ConSecciones' ? ' (' + i.secciones + ')' : '') + (i.mensaje && i.resultado === 'Error' ? ': ' + i.mensaje : '');
            lista.appendChild(li);
        });
    }

    function preguntar() {
        fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' }, cache: 'no-store' })
            .then(function (r) { return r.json(); })
            .then(function (d) {
                fallos = 0;
                if (!d.hay) return;
                pintar(d);
                if (d.activa) setTimeout(preguntar, 2000);
                else window.location.reload();   // terminó: se recarga para ver el resumen y el panorama actualizado
            })
            .catch(function () {
                // Si el servidor no responde (se cerró la aplicación) se deja de preguntar tras unos intentos.
                if (++fallos < 5) setTimeout(preguntar, 3000);
                else fase.textContent = 'No se pudo consultar el avance. Recarga la página.';
            });
    }

    setTimeout(preguntar, 1000);
})();
