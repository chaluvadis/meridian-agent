window.meridianMap = (function () {
    const instances = new WeakMap();

    function create(element) {
        if (instances.has(element)) {
            return;
        }

        const map = L.map(element, { zoomControl: true, scrollWheelZoom: true });
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; OpenStreetMap contributors'
        }).addTo(map);

        map.setView([0, 0], 2);

        const markers = L.layerGroup().addTo(map);
        instances.set(element, { map, markers, markerRefs: [] });
    }

    function setMarkers(element, markers) {
        const instance = instances.get(element);
        if (!instance) {
            return;
        }

        instance.markers.clearLayers();
        instance.markerRefs = [];

        const points = [];

        markers.forEach(function (marker) {
            const latLng = [marker.lat, marker.lon];
            points.push(latLng);

            const icon = L.divIcon({
                className: 'meridian-marker ' + marker.kind,
                html: '<span></span>',
                iconSize: [16, 16],
                iconAnchor: [8, 8]
            });

            const leafletMarker = L.marker(latLng, { icon: icon, title: marker.label })
                .bindPopup(marker.label)
                .addTo(instance.markers);

            instance.markerRefs.push(leafletMarker);
        });

        if (points.length > 0) {
            instance.map.fitBounds(points, { padding: [32, 32], maxZoom: 16 });
        }
    }

    function dispose(element) {
        const instance = instances.get(element);
        if (!instance) {
            return;
        }

        instance.map.remove();
        instances.delete(element);
    }

    return { create, setMarkers, dispose };
})();
