// Sustained ramp: 0 -> 2,000 req/s over 1 minute, held for 1 minute, ramped back down.
// Measures the honest sustained read QPS this stack can hold — the browse path (GET
// /products) is the real "2,000 QPS" target, since a flash sale is read-dominated (many
// browsers, far fewer buyers). A second, much smaller scenario mixes in checkout attempts at
// 80/s — above the 50/s admission limit (Program.cs "checkout" rate limiter) — specifically to prove the rate limiter
// engages (429s) under load rather than the checkout path falling over.
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';
import { BASE_URL, uuid4 } from './common.js';

const checkoutOutcomes = new Counter('checkout_outcomes');

export const options = {
    scenarios: {
        browse: {
            executor: 'ramping-arrival-rate',
            exec: 'browse',
            startRate: 0,
            timeUnit: '1s',
            preAllocatedVUs: 300,
            maxVUs: 600,
            stages: [
                { target: 2000, duration: '60s' },
                { target: 2000, duration: '60s' },
                { target: 0, duration: '20s' },
            ],
        },
        checkout_attempts: {
            executor: 'constant-arrival-rate',
            exec: 'checkout',
            rate: 80,
            timeUnit: '1s',
            duration: '120s',
            preAllocatedVUs: 50,
            maxVUs: 100,
        },
    },
    thresholds: {
        // The browse path must stay healthy — this is the actual capacity claim being tested.
        'http_req_failed{scenario:browse}': ['rate<0.01'],
    },
};

let productId;

export function setup() {
    const res = http.get(`${BASE_URL}/api/v1/shop/products`);
    const products = res.json();
    productId = products[0].id;
    return { productId };
}

export function browse(data) {
    const res = http.get(`${BASE_URL}/api/v1/shop/products`);
    check(res, { 'browse 200': (r) => r.status === 200 });
}

export function checkout(data) {
    const cartToken = uuid4();
    const headers = { 'X-Cart-Token': cartToken, 'Content-Type': 'application/json' };

    http.post(
        `${BASE_URL}/api/v1/shop/cart/items`,
        JSON.stringify({ productId: data.productId, quantity: 1 }),
        { headers }
    );

    const res = http.post(
        `${BASE_URL}/api/v1/shop/checkout`,
        JSON.stringify({
            successUrl: 'https://example.com/ok',
            cancelUrl: 'https://example.com/cancel',
            guestEmail: 'loadtest@example.com',
        }),
        { headers: { ...headers, 'Idempotency-Key': uuid4() } }
    );
    checkoutOutcomes.add(1, { status: String(res.status) });
}
