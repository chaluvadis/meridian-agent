// Finding out where the traveler actually is, with their permission. Everything else in the app
// works off the city they typed, which is fine for weather and useless for distance; this is the
// one thing that turns that into a position.
//
// Geolocation is a prompt, not a right: a refusal or a timeout is answered with null and the app
// carries on with the city name, because a permission box is never worth breaking the page over.

window.getCurrentPosition = () =>
    new Promise((resolve) => {
        if (!('geolocation' in navigator)) {
            resolve(null);
            return;
        }

        let settled = false;

        const finish = (position) => {
            if (settled) {
                return;
            }

            settled = true;
            resolve(position);
        };

        // A prompt the traveler ignores must not hang the button forever.
        const timer = setTimeout(() => finish(null), 15000);

        navigator.geolocation.getCurrentPosition(
            (position) => {
                clearTimeout(timer);
                finish({
                    latitude: position.coords.latitude,
                    longitude: position.coords.longitude,
                });
            },
            () => {
                clearTimeout(timer);
                finish(null);
            },
            {
                enableHighAccuracy: false,
                timeout: 12000,
                maximumAge: 60000,
            }
        );
    });
