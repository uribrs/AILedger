# QRadar Events Pagination

Replace the hardcoded `LIMIT 100` used by IBM QRadar event searches with QRadar REST result pagination.

The implementation must use QRadar's documented `/ariel/searches/{search_id}/results` paging behavior instead of truncating the AQL search query.
