// Tema de color: automático (sigue prefers-color-scheme), claro u oscuro. La elección se recuerda; el script de <head>
// ya aplicó el tema antes de pintar, aquí se conecta el interruptor y se reacciona a los cambios del sistema.
(function () {
    var raiz = document.documentElement;
    var opciones = Array.prototype.slice.call(document.querySelectorAll('.selector-tema-opcion'));
    if (!opciones.length) { return; }
    var sistemaOscuro = window.matchMedia('(prefers-color-scheme: dark)');

    function leer() {
        try {
            var p = localStorage.getItem('tema');
            return p === 'claro' || p === 'oscuro' ? p : 'sistema';
        } catch (e) { return 'sistema'; }
    }

    function guardar(p) {
        try { localStorage.setItem('tema', p); } catch (e) { /* sin almacenamiento: vale para esta visita */ }
    }

    function aplicar(preferencia) {
        var oscuro = preferencia === 'oscuro' || (preferencia === 'sistema' && sistemaOscuro.matches);
        raiz.setAttribute('data-tema', oscuro ? 'oscuro' : 'claro');
        raiz.setAttribute('data-tema-preferencia', preferencia);
        opciones.forEach(function (o) { o.setAttribute('aria-pressed', o.dataset.tema === preferencia ? 'true' : 'false'); });
    }

    opciones.forEach(function (o) {
        o.addEventListener('click', function () { guardar(o.dataset.tema); aplicar(o.dataset.tema); });
    });

    // Si la preferencia es "sistema", se sigue al sistema en vivo (por ejemplo, al llegar la noche).
    sistemaOscuro.addEventListener('change', function () { if (leer() === 'sistema') { aplicar('sistema'); } });

    aplicar(leer());
})();

// Menú lateral: en escritorio se colapsa a solo íconos (y se recuerda); en móvil se abre como panel deslizable.
(function () {
    var raiz = document.documentElement;
    var boton = document.getElementById('btn-menu');
    var fondo = document.getElementById('menu-fondo');
    if (!boton) { return; }

    var esMovil = window.matchMedia('(max-width: 991.98px)');

    function recordar(colapsado) {
        try { localStorage.setItem('menu-colapsado', colapsado ? '1' : '0'); } catch (e) { /* sin almacenamiento: no pasa nada */ }
    }

    function abierto() {
        return esMovil.matches ? raiz.classList.contains('menu-abierto') : !raiz.classList.contains('menu-colapsado');
    }

    function refrescar() { boton.setAttribute('aria-expanded', abierto() ? 'true' : 'false'); }

    function cerrarMovil() { raiz.classList.remove('menu-abierto'); refrescar(); }

    boton.addEventListener('click', function () {
        if (esMovil.matches) {
            raiz.classList.toggle('menu-abierto');
        } else {
            recordar(raiz.classList.toggle('menu-colapsado'));
        }
        refrescar();
    });

    if (fondo) { fondo.addEventListener('click', cerrarMovil); }
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && esMovil.matches) { cerrarMovil(); } });

    // Al elegir una sección en móvil el panel se cierra; al cambiar el tamaño de la ventana se limpia el estado.
    document.querySelectorAll('.menu-item').forEach(function (a) { a.addEventListener('click', function () { if (esMovil.matches) { cerrarMovil(); } }); });
    esMovil.addEventListener('change', function () { raiz.classList.remove('menu-abierto'); refrescar(); });

    refrescar();
})();

// "Actualizar desde Banner": puede tardar (espera tu login); se muestra un aviso y se evita el doble clic.
document.querySelectorAll('form[data-sync]').forEach(function (form) {
    form.addEventListener('submit', function (e) {
        if (form.dataset.enviado === '1') { e.preventDefault(); return; }
        form.dataset.enviado = '1';
        var boton = form.querySelector('button[type=submit]');
        if (boton) { boton.disabled = true; }
        var overlay = document.getElementById('sync-overlay');
        if (overlay) { overlay.hidden = false; }
    });
});

// Mapa del pénsum: al hacer clic en una tarjeta se muestra su detalle en un cuadro modal.
(function () {
    var modalEl = document.getElementById('modalMateria');
    if (!modalEl) { return; }
    var modal = new bootstrap.Modal(modalEl);
    var campo = function (id) { return modalEl.querySelector('#' + id); };

    document.querySelectorAll('.mapa-card').forEach(function (card) {
        card.addEventListener('click', function () {
            var d = card.dataset;
            campo('modalTitulo').textContent = d.codigo + ' – ' + d.nombre;
            campo('modalEstado').textContent = d.estado;
            campo('modalEstado').className = 'badge ' + d.badge;
            campo('modalCreditos').textContent = d.creditos;
            campo('modalCuatrimestre').textContent = d.cuatrimestre;
            campo('modalPrerrequisitos').textContent = d.prerrequisitos;

            var detalle = campo('modalDetalle');
            detalle.innerHTML = '';
            (d.detalle ? d.detalle.split('|') : []).forEach(function (linea) {
                var li = document.createElement('li');
                li.textContent = linea;
                detalle.appendChild(li);
            });
            campo('modalDetalleBloque').hidden = detalle.children.length === 0;
            campo('modalFicha').href = d.url;
            modal.show();
        });
    });
})();

// Números que cuentan desde 0 al cargar (tarjetas y centro de la dona). El valor final ya viene escrito en el HTML:
// sin JavaScript, o con "reducir movimiento", se muestra directamente sin animar.
(function () {
    var elementos = document.querySelectorAll('[data-contar]');
    if (!elementos.length) { return; }
    var reducir = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    var duracion = 900;

    elementos.forEach(function (el) {
        var destino = parseFloat(el.dataset.contar);
        var decimales = parseInt(el.dataset.decimales || '0', 10);
        var sufijo = el.dataset.sufijo || '';
        if (isNaN(destino) || reducir) { return; }

        function texto(v) { return v.toFixed(decimales) + sufijo; }

        var inicio = null;
        el.textContent = texto(0);
        function paso(ahora) {
            if (inicio === null) { inicio = ahora; }
            var avance = Math.min((ahora - inicio) / duracion, 1);
            var suave = 1 - Math.pow(1 - avance, 3);   // frena al final
            el.textContent = texto(destino * suave);
            if (avance < 1) { requestAnimationFrame(paso); } else { el.textContent = texto(destino); }
        }
        requestAnimationFrame(paso);
    });
})();

// Mapa del pénsum: al pasar el cursor o enfocar una materia se resaltan sus requisitos y lo que desbloquea.
(function () {
    var mapa = document.querySelector('.mapa');
    var resumen = document.getElementById('mapa-resumen');
    if (!mapa || !resumen) { return; }

    var tarjetas = Array.prototype.slice.call(mapa.querySelectorAll('.mapa-card'));
    var porCodigo = {};
    tarjetas.forEach(function (t) { porCodigo[t.dataset.codigo] = t; });
    var textoInicial = resumen.dataset.inicial;

    function lista(valor) { return valor ? valor.split(',').filter(Boolean) : []; }

    function resaltar(tarjeta) {
        var requisitos = lista(tarjeta.dataset.prereq);
        var desbloquea = lista(tarjeta.dataset.desbloquea);
        mapa.classList.add('mapa-enfoque');
        tarjeta.classList.add('mapa-actual');
        requisitos.forEach(function (c) { if (porCodigo[c]) { porCodigo[c].classList.add('mapa-requisito'); } });
        desbloquea.forEach(function (c) { if (porCodigo[c]) { porCodigo[c].classList.add('mapa-desbloquea'); } });

        var req = tarjeta.dataset.prerrequisitos && tarjeta.dataset.prerrequisitos !== '—' ? tarjeta.dataset.prerrequisitos : 'ninguno';
        resumen.textContent = tarjeta.dataset.codigo + ' – ' + tarjeta.dataset.nombre + '. Requiere: ' + req +
            '. Desbloquea: ' + (desbloquea.length ? desbloquea.join(', ') : 'ninguna materia') + '.';
    }

    function limpiar() {
        mapa.classList.remove('mapa-enfoque');
        tarjetas.forEach(function (t) { t.classList.remove('mapa-actual', 'mapa-requisito', 'mapa-desbloquea'); });
        resumen.textContent = textoInicial;
    }

    tarjetas.forEach(function (t) {
        t.addEventListener('mouseenter', function () { resaltar(t); });
        t.addEventListener('focus', function () { resaltar(t); });
        t.addEventListener('mouseleave', limpiar);
        t.addEventListener('blur', limpiar);
    });
})();
