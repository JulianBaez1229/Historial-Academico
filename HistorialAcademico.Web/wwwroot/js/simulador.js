// Simulador de índice: con la nota que se espera sacar en cada materia recalcula, al instante, el índice proyectado y el promedio que
// hace falta en el resto para llegar al índice objetivo. Las cuentas y las frases son las mismas de SimuladorIndice.cs (Core): si se
// cambia una, hay que cambiar la otra (una prueba compara lo que dice el servidor con lo que dice este archivo).
(function () {
    var sim = document.getElementById('simulador');
    if (!sim) { return; }

    var escala = JSON.parse(sim.dataset.escala);
    var h0 = parseFloat(sim.dataset.horas), p0 = parseFloat(sim.dataset.puntos);
    var sinFila = parseFloat(sim.dataset.sinFila) || 0;
    var minP = parseFloat(sim.dataset.minimo), maxP = parseFloat(sim.dataset.maximo);
    var letraMaxima = sim.dataset.letraMaxima;
    var url = sim.dataset.url;
    var token = document.querySelector('#plan-token input[name=__RequestVerificationToken]');

    var salidaProyectado = document.getElementById('sim-proyectado');
    var salidaDetalle = document.getElementById('sim-detalle');
    var salidaNecesidad = document.getElementById('sim-necesidad');
    var objetivoInput = document.getElementById('sim-objetivo');
    var guardado = document.getElementById('sim-guardado');

    function n2(v) { return v.toFixed(2); }
    function cr(v) { return String(parseFloat(v.toFixed(2))); }
    // Redondeo a dos decimales «hacia afuera del cero» (como decimal en C#): sin errores de coma flotante en los empates.
    function indice(puntos, horas) { return horas === 0 ? 0 : Math.floor((200 * puntos + horas) / (2 * horas)) / 100; }
    function buscar(letra) { for (var i = 0; i < escala.length; i++) { if (escala[i].l.toLowerCase() === String(letra).toLowerCase()) { return escala[i]; } } return null; }

    function calcular() {
        var objetivo = parseFloat(objetivoInput.value);
        if (isNaN(objetivo)) { objetivo = parseFloat(sim.dataset.objetivo); }
        var horas = h0, puntos = p0, conNota = 0, porDefinir = sinFila;

        document.querySelectorAll('.sim-nota').forEach(function (s) {
            var creditos = parseFloat(s.dataset.creditos);
            if (!(creditos > 0)) { return; }
            var letra = s.value ? buscar(s.value) : null;
            if (!letra) { porDefinir += creditos; return; }
            if (!letra.c) { return; }
            horas += creditos; puntos += letra.p * creditos; conNota += creditos;
        });

        var proyectado = indice(puntos, horas);
        var maximo = indice(puntos + maxP * porDefinir, horas + porDefinir);
        var texto;
        if (porDefinir === 0) {
            texto = 'Le pusiste nota a todo lo que falta: tu índice quedaría en ' + n2(proyectado) + ', ' + (proyectado >= objetivo ? 'que alcanza' : 'que no alcanza') + ' el objetivo de ' + n2(objetivo) + '.';
        } else {
            var necesario = (objetivo * (horas + porDefinir) - puntos) / porDefinir;
            if (indice(puntos + minP * porDefinir, horas + porDefinir) >= objetivo) {
                texto = 'El objetivo de ' + n2(objetivo) + ' ya lo tienes asegurado: se alcanza aunque lo que falta (' + cr(porDefinir) + ' créditos) salga con la nota más baja.';
            } else if (maximo < objetivo) {
                texto = 'El objetivo de ' + n2(objetivo) + ' no se alcanza: ni con ' + letraMaxima + ' en todo lo que falta (' + cr(porDefinir) + ' créditos) llegarías a más de ' + n2(maximo) + '.';
            } else {
                var letras = escala.filter(function (l) { return l.c && l.p >= necesario; }).sort(function (a, b) { return a.p - b.p; });
                texto = 'Para llegar a ' + n2(objetivo) + ' necesitas un promedio de ' + n2(Math.round(necesario * 100 + 1e-9) / 100) + ' en los ' + cr(porDefinir) + ' créditos que faltan por definir (por ejemplo, ' + (letras.length ? letras[0].l : '') + ' en todo).';
            }
        }

        salidaProyectado.textContent = n2(proyectado);
        salidaDetalle.textContent = 'con ' + cr(conNota) + ' créditos con nota esperada';
        salidaNecesidad.textContent = texto;
    }

    function guardar(select) {
        if (!url) { return; }
        var datos = new FormData();
        datos.append('codigo', select.dataset.codigo);
        datos.append('nota', select.value);
        datos.append('__RequestVerificationToken', token ? token.value : '');
        fetch(url, { method: 'POST', body: datos, headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) { return r.json().then(function (j) { return { ok: r.ok, json: j }; }); })
            .then(function (r) { if (guardado) { guardado.textContent = r.ok ? 'Guardado: ' + r.json.mensaje : (r.json.mensaje || 'No se pudo guardar la nota.'); } })
            .catch(function () { if (guardado) { guardado.textContent = 'No se pudo guardar la nota. Revisa que la aplicación siga corriendo.'; } });
    }

    document.querySelectorAll('.sim-nota').forEach(function (s) {
        s.addEventListener('change', function () { calcular(); guardar(s); });
    });

    try {
        var recordado = localStorage.getItem('planificador-objetivo');
        if (recordado && !isNaN(parseFloat(recordado))) { objetivoInput.value = recordado; }
    } catch (e) { /* sin almacenamiento: se usa el objetivo por omisión */ }
    objetivoInput.addEventListener('input', function () {
        try { localStorage.setItem('planificador-objetivo', objetivoInput.value); } catch (e) { /* ignorar */ }
        calcular();
    });
    calcular();
})();
