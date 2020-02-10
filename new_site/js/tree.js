iterate = () => {
    for (var t = 0; t < 100; t++) {
        var a = calculate();
        x1 = x * a.a + y * a.b + a.tx,
        y1 = x * a.c + y * a.d + a.ty,
        x = x1,
        y = y1,
        draw(x, y, 300)
    }
    window.requestAnimationFrame(iterate)
}

calculate = () => {
    for (var t = Math.random(), a = 0; a < coords.length; a++) {
        var e = coords[a];
        if (t < e.w)
            return e;
        t -= e.w
    }
}

draw = (t, a, e) => {
    c.fillRect(t * e, -a * e, .5, .5)
}

let canvas = document.getElementById("canvas"),
    w = innerWidth,
    h = innerHeight,
    c = canvas.getContext("2d"),
    x = Math.random(),
    y = Math.random(),
    coords = [{
        a: .05,
        b: 0,
        c: 0,
        d: .6,
        tx: 0,
        ty: 0,
        w: .17
    }, {
        a: .05,
        b: 0,
        c: 0,
        d: -.5,
        tx: 0,
        ty: 1,
        w: .17
    }, {
        a: .46,
        b: -.321,
        c: .386,
        d: .383,
        tx: 0,
        ty: .6,
        w: .17
    }, {
        a: .47,
        b: -.154,
        c: .171,
        d: .423,
        tx: 0,
        ty: 1.1,
        w: .17
    }, {
        a: .433,
        b: .275,
        c: -.25,
        d: .476,
        tx: 0,
        ty: 1,
        w: .16
    }, {
        a: .421,
        b: .257,
        c: -.353,
        d: .306,
        tx: 0,
        ty: .7,
        w: .16
    }];

canvas.width = w
canvas.height = h
c.translate(w / 2, h)

iterate()
