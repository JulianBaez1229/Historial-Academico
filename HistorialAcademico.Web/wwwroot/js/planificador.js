// Planificador: mover materias entre períodos.
//  · Con el ratón se arrastran las tarjetas (arrastrar y soltar).
//  · Con el teclado o tocando la pantalla se usa «Mover…»: se elige la materia, luego el período (botón «Mover aquí»).
// En los dos casos, mientras se elige, las columnas donde la materia NO puede ir se atenúan y dicen por qué (prerrequisito, porcentaje,
// período pasado…), y las que pasarían el límite de créditos avisan. Cada movimiento se guarda en el servidor (la misma acción que el botón
// «Agregar a…») y se recarga la página para volver a validar el plan.
(function () {
    var tablero = document.getElementById('tablero');
    if (!tablero) { return; }

    var token = document.querySelector('#plan-token input[name=__RequestVerificationToken]');
    var planId = tablero.dataset.plan;
    var url = tablero.dataset.url;
    var estado = document.getElementById('plan-estado');
    var banner = document.getElementById('plan-modo');
    var bannerTexto = document.getElementById('plan-modo-texto');
    var colocaciones = {};
    try { colocaciones = JSON.parse(tablero.dataset.colocaciones || '{}'); } catch (e) { colocaciones = {}; }

    var columnas = Array.prototype.slice.call(document.querySelectorAll('.plan-col[data-periodo]'));
    var bandeja = document.querySelector('.lista-pendientes');

    // ── Posición de la pantalla ───────────────────────────────────────────────────────────
    var contenedorColumnas = document.querySelector('.plan-columnas');
    try {
        var guardado = JSON.parse(sessionStorage.getItem('planificador-scroll') || 'null');
        if (guardado) {
            window.scrollTo(0, guardado.y);
            if (contenedorColumnas) { contenedorColumnas.scrollLeft = guardado.x; }
            sessionStorage.removeItem('planificador-scroll');
        }
    } catch (e) { /* sessionStorage no disponible: no pasa nada */ }

    function recordarPosicion() {
        try {
            sessionStorage.setItem('planificador-scroll', JSON.stringify({ y: window.scrollY, x: contenedorColumnas ? contenedorColumnas.scrollLeft : 0 }));
        } catch (e) { /* ignorar */ }
    }

    function anunciar(texto) { if (estado) { estado.textContent = ''; setTimeout(function () { estado.textContent = texto; }, 30); } }

    // ── Qué se puede y qué no ─────────────────────────────────────────────────────────────
    function infoDe(codigo, periodo) {
        var porPeriodo = colocaciones[String(codigo).toUpperCase()] || {};
        return porPeriodo[periodo] || null;
    }
    function motivosDe(codigo, periodo) { var i = infoDe(codigo, periodo); return i && i.m && i.m.length ? i.m : null; }

    function marcar(codigo) {
        columnas.forEach(function (col) {
            var periodo = col.dataset.periodo;
            var info = infoDe(codigo, periodo);
            var motivos = info && info.m && info.m.length ? info.m : null;
            var motivo = col.querySelector('[data-motivo]');
            var aviso = col.querySelector('[data-aviso]');
            col.classList.toggle('no-permitida', !!motivos);
            motivo.textContent = motivos ? 'No puede ir aquí: ' + motivos.join(' ') : '';
            motivo.hidden = !motivos;
            aviso.textContent = !motivos && info && info.a ? info.a : '';
            aviso.hidden = !(!motivos && info && info.a);
        });
    }

    function limpiar() {
        columnas.forEach(function (col) {
            col.classList.remove('no-permitida');
            col.querySelector('[data-motivo]').hidden = true;
            col.querySelector('[data-aviso]').hidden = true;
        });
    }

    // ── Guardar ───────────────────────────────────────────────────────────────────────────
    function asignar(codigo, periodo) {
        var datos = new FormData();
        datos.append('id', planId);
        datos.append('codigo', codigo);
        datos.append('periodo', periodo);
        datos.append('__RequestVerificationToken', token ? token.value : '');

        fetch(url, { method: 'POST', body: datos, headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) { return r.json().then(function (j) { return { ok: r.ok, json: j }; }); })
            .then(function (r) {
                if (!r.ok) { alert(r.json.mensaje || 'No se pudo mover la materia.'); return; }
                recordarPosicion();
                window.location.reload();
            })
            .catch(function () { alert('No se pudo guardar el cambio. Revisa que la aplicación siga corriendo.'); });
    }

    // ── Arrastrar con el ratón ────────────────────────────────────────────────────────────
    document.querySelectorAll('.plan-card[draggable=true]').forEach(function (card) {
        card.addEventListener('dragstart', function (e) {
            e.dataTransfer.setData('text/plain', card.dataset.codigo);
            e.dataTransfer.effectAllowed = 'move';
            card.classList.add('arrastrando');
            marcar(card.dataset.codigo);
        });
        card.addEventListener('dragend', function () { card.classList.remove('arrastrando'); limpiar(); });
    });

    document.querySelectorAll('[data-dropzone]').forEach(function (zona) {
        function columnaDe() { return zona.closest('.plan-col'); }
        zona.addEventListener('dragover', function (e) {
            var col = columnaDe();
            if (col && col.classList.contains('no-permitida')) { e.dataTransfer.dropEffect = 'none'; return; }   // sin preventDefault: no se puede soltar
            e.preventDefault();
            zona.classList.add('drop-activa');
        });
        zona.addEventListener('dragleave', function () { zona.classList.remove('drop-activa'); });
        zona.addEventListener('drop', function (e) {
            e.preventDefault();
            zona.classList.remove('drop-activa');
            var col = columnaDe();
            if (col && col.classList.contains('no-permitida')) { return; }
            var codigo = e.dataTransfer.getData('text/plain');
            if (codigo) { asignar(codigo, zona.dataset.periodo || ''); }
        });
    });

    // ── «Mover…»: teclado y pantallas táctiles ────────────────────────────────────────────
    var moviendo = null;   // { codigo, boton }

    function destinosVisibles() {
        return Array.prototype.slice.call(document.querySelectorAll('[data-destino]')).filter(function (b) { return !b.hidden; });
    }

    function iniciarMovimiento(boton) {
        cancelarMovimiento(false);
        var codigo = boton.dataset.codigo;
        moviendo = { codigo: codigo, boton: boton };
        var tarjeta = boton.closest('.plan-card');
        if (tarjeta) { tarjeta.classList.add('seleccionada'); }
        marcar(codigo);

        var enPlan = !!boton.closest('.plan-col');
        var permitidas = [];
        columnas.forEach(function (col) {
            var b = col.querySelector('[data-destino]');
            var permitida = !motivosDe(codigo, col.dataset.periodo);
            b.hidden = !permitida;
            b.setAttribute('aria-label', 'Mover ' + codigo + ' a ' + col.dataset.periodo);
            if (permitida) { permitidas.push(col.dataset.periodo); }
        });
        var volver = document.querySelector('[data-bandeja]');
        if (volver) { volver.hidden = !enPlan; }

        var texto = 'Moviendo ' + codigo + ': elige un período con «Mover aquí»' + (enPlan ? ' o devuélvela a «Por planificar»' : '') + '. Esc cancela.';
        bannerTexto.textContent = texto;
        banner.hidden = false;
        anunciar(texto + (permitidas.length ? ' Períodos posibles: ' + permitidas.join(', ') + '.' : ' Por ahora no puede ir a ningún período.'));
        var primero = destinosVisibles()[0];
        if (primero) { primero.focus(); }
    }

    function cancelarMovimiento(devolverFoco) {
        if (!moviendo) { return; }
        var boton = moviendo.boton;
        var tarjeta = boton.closest('.plan-card');
        if (tarjeta) { tarjeta.classList.remove('seleccionada'); }
        moviendo = null;
        limpiar();
        document.querySelectorAll('[data-destino]').forEach(function (b) { b.hidden = true; });
        banner.hidden = true;
        if (devolverFoco) { boton.focus(); anunciar('Movimiento cancelado.'); }
    }

    document.querySelectorAll('[data-mover]').forEach(function (b) {
        b.addEventListener('click', function (e) { e.preventDefault(); iniciarMovimiento(b); });
    });
    document.querySelectorAll('[data-destino]').forEach(function (b) {
        b.addEventListener('click', function () {
            if (!moviendo) { return; }
            var codigo = moviendo.codigo;
            anunciar('Moviendo ' + codigo + '…');
            asignar(codigo, b.dataset.periodo || '');
        });
    });
    var cancelar = document.getElementById('plan-modo-cancelar');
    if (cancelar) { cancelar.addEventListener('click', function () { cancelarMovimiento(true); }); }

    document.addEventListener('keydown', function (e) {
        if (!moviendo) { return; }
        if (e.key === 'Escape') { e.preventDefault(); cancelarMovimiento(true); return; }
        // Flechas: pasar de un destino posible al siguiente.
        if (['ArrowRight', 'ArrowDown', 'ArrowLeft', 'ArrowUp'].indexOf(e.key) >= 0) {
            var lista = destinosVisibles();
            var i = lista.indexOf(document.activeElement);
            if (i < 0) { return; }
            e.preventDefault();
            var siguiente = (e.key === 'ArrowRight' || e.key === 'ArrowDown') ? Math.min(i + 1, lista.length - 1) : Math.max(i - 1, 0);
            lista[siguiente].focus();
        }
    });

    // ── Filtrar la bandeja ────────────────────────────────────────────────────────────────
    var chips = Array.prototype.slice.call(document.querySelectorAll('.filtro-chip'));
    var buscar = document.getElementById('buscar-bandeja');
    var vacio = document.getElementById('sin-coincidencias');
    var filtro = 'todas';
    try { filtro = sessionStorage.getItem('planificador-filtro') || 'todas'; } catch (e) { filtro = 'todas'; }
    if (!chips.some(function (c) { return c.dataset.filtro === filtro; })) { filtro = 'todas'; }

    function aplicarFiltro() {
        var texto = buscar ? buscar.value.trim().toLowerCase() : '';
        var visibles = 0;
        chips.forEach(function (c) { c.setAttribute('aria-pressed', c.dataset.filtro === filtro ? 'true' : 'false'); });
        document.querySelectorAll('.lista-pendientes .plan-card').forEach(function (card) {
            var etiquetas = (card.dataset.etiquetas || '').split(' ');
            var coincide = (filtro === 'todas' || etiquetas.indexOf(filtro) >= 0) && (!texto || (card.dataset.buscar || '').indexOf(texto) >= 0);
            card.hidden = !coincide;
            if (coincide) { visibles++; }
        });
        if (vacio) { vacio.hidden = visibles > 0 || chips.length === 0; }
    }
    chips.forEach(function (c) {
        c.addEventListener('click', function () {
            filtro = c.dataset.filtro;
            try { sessionStorage.setItem('planificador-filtro', filtro); } catch (e) { /* ignorar */ }
            aplicarFiltro();
            anunciar('Filtro: ' + c.textContent.trim() + '.');
        });
    });
    if (buscar) { buscar.addEventListener('input', aplicarFiltro); }
    aplicarFiltro();
})();
