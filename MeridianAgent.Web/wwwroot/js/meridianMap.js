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

    function setMarkers(element, markers) {        const instance = instances.get(element);
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

    // Pans to the named place and opens its popup. The label can arrive either as the bare venue
    // name ("Karak House") or as the natural-language payload a quick action chip carries ("Plan an
    // evening around Karak House"), so accept an exact match, a title that ends the phrase, and a
    // title that appears anywhere inside it.
    function focusMarker(element, label) {
        const instance = instances.get(element);
        if (!instance) {
            return;
        }

        const wanted = (label || '').trim().toLowerCase();
        if (!wanted) {
            return;
        }

        const match = instance.markerRefs.find(function (marker) {
            const title = (marker.options.title || '').trim().toLowerCase();
            return matchesVenueTitle(title, wanted);
        });

        if (!match) {
            return;
        }

        instance.map.flyTo(match.getLatLng(), Math.max(instance.map.getZoom(), 15), { duration: 0.6 });
        match.openPopup();
    }

    function matchesVenueTitle(title, wanted) {
        if (!title || !wanted) {
            return false;
        }

        if (title === wanted || title.endsWith('. ' + wanted) || wanted.endsWith(' ' + title)) {
            return true;
        }

        // The map numbers its markers ("1. Karak House") while the chip payload carries the bare
        // venue name, so compare on the unnumbered title as well.
        const bare = title.replace(/^\d+\.\s*/, '');
        return bare.length >= 6 && wanted.indexOf(bare) !== -1;
    }

    function dispose(element) {
        const instance = instances.get(element);
        if (!instance) {
            return;
        }

        instance.map.remove();
        instances.delete(element);
    }

    return { create, setMarkers, focusMarker, dispose };
})();
