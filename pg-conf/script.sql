CREATE UNLOGGED TABLE "Transactions" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Amout" NUMERIC(18,2) NOT NULL,
    "requestedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT now(),
    "Gateway" INT NOT NULL
);

CREATE INDEX idx_transactions_covering_stats ON "Transactions" ("requestedAt", "Gateway") INCLUDE ("Amout");