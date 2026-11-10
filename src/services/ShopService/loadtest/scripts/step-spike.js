// Step spike: 0 -> 2,000 req/s in ~5s (as close to instantaneous as k6's arrival-rate executor
// allows), held for 30s, dropped. This is the literal "doorbuster" moment — tests whether
// Kestrel/the .NET thread pool, Npgsql's connection pool, and the memory/Redis cache absorb a sudden step function
// instead of queueing requests into a pile-up.
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL } from './common.js';

export const options = {
    scenarios: {
        spike: {
            executor: 'ramping-arrival-rate',
            startRate: 0,
            timeUnit: '1s',
            preAllocatedVUs: 400,
            maxVUs: 800,
            stages: [
                { target: 2000, duration: '5s' },
                { target: 2000, duration: '30s' },
                { target: 0, duration: '10s' },
            ],
        },
    },
};

export default function () {
    const res = http.get(`${BASE_URL}/api/v1/shop/products`);
    check(res, { '200': (r) => r.status === 200 });
}
