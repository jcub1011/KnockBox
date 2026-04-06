/**
 * Generates a barrel distortion displacement map and injects it into an SVG
 * feImage element. Uses asin/cos spherical mapping for authentic CRT curvature.
 * The distortion extends to all corners using the diagonal as the radius.
 *
 * Based on: https://codepen.io/mullany/pen/ZKoqLB
 */
function initBarrelDistortion(feImageId, size) {
    var canvas = document.createElement('canvas');
    canvas.width = size;
    canvas.height = size;
    var ctx = canvas.getContext('2d');
    var imgData = ctx.createImageData(size, size);
    var data = imgData.data;
    var half = size / 2;
    // Use diagonal so the sphere reaches all corners
    var radius = Math.sqrt(half * half + half * half);

    for (var y = 0; y < size; y++) {
        for (var x = 0; x < size; x++) {
            var dx = x - half;
            var dy = y - half;
            var l = Math.sqrt(dx * dx + dy * dy);
            var nl = l / radius; // normalize to [0, 1] over diagonal
            var a = Math.asin(Math.min(nl, 1));
            var z = 255 - Math.cos(a) * 255;
            var r = 128 + (dx / radius) * (z / 255) * 128;
            var g = 128 + (dy / radius) * (z / 255) * 128;

            var idx = (y * size + x) * 4;
            data[idx]     = Math.floor(Math.max(0, Math.min(255, r)));
            data[idx + 1] = Math.floor(Math.max(0, Math.min(255, g)));
            data[idx + 2] = 0;
            data[idx + 3] = 255;
        }
    }

    ctx.putImageData(imgData, 0, 0);

    var el = document.getElementById(feImageId);
    if (el) {
        el.setAttribute('href', canvas.toDataURL());
    }
}
