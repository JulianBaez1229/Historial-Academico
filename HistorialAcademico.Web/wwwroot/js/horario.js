// Comodidades de las pantallas «Horas no disponibles» y «Solicitar apertura». Todo funciona sin JavaScript:
// esto solo agrega atajos (los botones nacen ocultos) y el botón de copiar.
(function () {
    'use strict';

    // ── Horas no disponibles ────────────────────────────────────────────────────────────
    var tabla = document.getElementById('cuadricula');
    if (tabla) {
        var casillas = Array.prototype.slice.call(tabla.querySelectorAll('input[type=checkbox]'));
        var atajos = document.getElementById('atajos');
        if (atajos) atajos.hidden = false;

        function marcar(filtro, valor) {
            casillas.forEach(function (c) { if (filtro(c)) c.checked = valor; });
        }

        if (atajos) atajos.addEventListener('click', function (e) {
            var boton = e.target.closest('button[data-atajo]');
            if (!boton) return;
            if (boton.getAttribute('data-atajo') === 'limpiar') marcar(function () { return true; }, false);
            else marcar(function (c) {
                var h = parseInt(c.getAttribute('data-hora'), 10);
                return ['Lunes', 'Martes', 'Miercoles', 'Jueves', 'Viernes'].indexOf(c.getAttribute('data-dia')) >= 0 && h >= 8 && h < 17;
            }, true);
        });

        // Un clic en el nombre del día marca toda la columna (o la desmarca si ya estaba completa).
        tabla.addEventListener('click', function (e) {
            var boton = e.target.closest('button[data-columna]');
            if (!boton) return;
            var dia = boton.getAttribute('data-columna');
            var delDia = casillas.filter(function (c) { return c.getAttribute('data-dia') === dia; });
            var todas = delDia.every(function (c) { return c.checked; });
            delDia.forEach(function (c) { c.checked = !todas; });
        });
    }

    // ── Copiar el texto de la solicitud ─────────────────────────────────────────────────
    var copiar = document.getElementById('copiar-texto');
    var texto = document.getElementById('texto-solicitud');
    if (copiar && texto) {
        copiar.hidden = false;
        var aviso = document.getElementById('copiado');
        copiar.addEventListener('click', function () {
            function listo() { if (aviso) aviso.textContent = 'Texto copiado.'; }
            function respaldo() {
                texto.select();
                try { document.execCommand('copy'); listo(); } catch (e) { if (aviso) aviso.textContent = 'Selecciona el texto y cópialo con Ctrl+C.'; }
            }
            if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(texto.value).then(listo, respaldo);
            else respaldo();
        });
    }
})();
