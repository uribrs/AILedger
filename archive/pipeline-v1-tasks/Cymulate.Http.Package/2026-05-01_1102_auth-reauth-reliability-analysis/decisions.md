* Use full task state because this is a multi-step architecture and reliability analysis.
* Treat OAuth2, JWT, username/password token exchange, generic token exchange, bearer token, API key, basic auth, HMAC, negotiated auth, authentication provider wrappers, and session retry integration as in-scope until code review narrows the set.
* Use official or standards-body sources for protocol-level guidance and implementation guidance.
* Refreshable mechanisms for the final report are OAuth2 client credentials, generic token exchange, username/password token exchange, JWT with refresh delegate, and negotiated authentication when its winner can refresh or renegotiate.
* Non-refreshable static mechanisms are API key, basic, HMAC, static bearer, none, and negotiated authentication after choosing a non-refreshable winner.
