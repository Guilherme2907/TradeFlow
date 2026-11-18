FROM quay.io/keycloak/keycloak:26.1.0 AS builder

ENV KC_HEALTH_ENABLED=true
ENV KC_METRICS_ENABLED=true
ENV KC_DB=postgres

# Bake custom white-label TradeFlow theme and initial realm definition into the image
COPY docker/keycloak/themes/tradeflow /opt/keycloak/themes/tradeflow
COPY docker/keycloak/tradeflow-realm.json /opt/keycloak/data/import/tradeflow-realm.json

RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:26.1.0
COPY --from=builder /opt/keycloak/ /opt/keycloak/

ENV KC_HEALTH_ENABLED=true
ENV KC_METRICS_ENABLED=true

ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
