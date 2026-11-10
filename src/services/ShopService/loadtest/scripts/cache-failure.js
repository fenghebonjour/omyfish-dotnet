// Sustained moderate read load (800 req/s, comfortably inside this stack's measured capacity)
// for 90s. Run alongside the driver script (run-cache-failure.sh), which stops the redis
// container ~30s in and restarts it ~30s later — this script just measures whether requests
// keep succeeding (ProductCatalogCache's fall-through-to-Postgres path) through the outage and
// recovery, not scripting the outage itself (k6 has no docker access).
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://shop-service:8080';

export const options = {
    scenarios: {
        browse: {
            executor: 'constant-arrival-rate',
            rate: 800,
            timeUnit: '1s',
            duration: '90s',
            preAllocatedVUs: 150,
            maxVUs: 300,
        },
    },
    thresholds: {
        // The whole point: no 500s, cache outage or not — a dead Redis must fall through to
        // the database, never surface as an error to the caller.
        http_req_failed: ['rate<0.01'],
    },
};

export default function () {
    const res = http.get(`${BASE_URL}/api/v1/shop/products`);
    check(res, { '200': (r) => r.status === 200 });
}
