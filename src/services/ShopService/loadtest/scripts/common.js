// Shared helpers for all three scenario scripts. No external k6 extensions — just plain JS,
// so the stock k6 Docker image (grafana/k6) runs these with no custom build.

export const BASE_URL = __ENV.BASE_URL || 'http://shop-service:8080';

// RFC4122-shaped v4 UUID — good enough for header/body values the app only ever treats as
// opaque identifiers (cart token, idempotency key), not for anything needing real randomness
// guarantees.
export function uuid4() {
    let d = Date.now();
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
        const r = (d + Math.random() * 16) % 16 | 0;
        d = Math.floor(d / 16);
        return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
    });
}
