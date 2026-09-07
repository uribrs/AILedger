* VALIDATED: QRadar result retrieval supports paging through a zero-based `Range: items=x-y` header on `GET /ariel/searches/{search_id}/results`.
* VALIDATED: QRadar paging responses can expose total result count through `Content-Range`, including `items x-y/total` and out-of-range `items */total`.
* VALIDATED: The existing test fake can validate request headers; the QRadar pagination test asserts `Range: items=0-499` and `Range: items=500-999`.
