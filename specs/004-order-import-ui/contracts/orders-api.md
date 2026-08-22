# Contract: Imported Orders Browse API

## `GET /api/orders`

Authenticated employees receive every imported order, sorted `importedAt` descending then `tcgplayerOrderId` ascending.

```json
[{"orderId":42,"tcgplayerOrderId":"A-100","status":"ready","importedAt":"2026-08-21T18:30:00Z"}]
```

`200 OK` with `[]` is the empty state; `401` means no valid session; `500` means the query failed. Status comes from actual `OrderStatus`. Responses MUST NOT contain order lines, customer name/address/contact information, or import-attempt details.
