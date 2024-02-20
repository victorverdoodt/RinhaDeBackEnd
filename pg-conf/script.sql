-- Criação da tabela Customers
CREATE TABLE "Customers" (
    "Id" SERIAL PRIMARY KEY,
    "Name" VARCHAR(255) NOT NULL,
    "Balance" INT NOT NULL,
    "Limit" INT NOT NULL
);

-- Criação da tabela Transactions
CREATE TABLE "Transactions" (
    "Id" SERIAL PRIMARY KEY,
    "Value" INT NOT NULL,
    "Type" CHAR NOT NULL,
    "Description" TEXT NOT NULL,
    "TransactionDate" TIMESTAMP NOT NULL,
    "CustomerId" INT NOT NULL,
    FOREIGN KEY ("CustomerId") REFERENCES "Customers"("Id")
);

-- Criação de um índice ordenado na coluna TransactionDate da tabela Transactions
CREATE INDEX idx_transactiondate_id ON "Transactions" ("TransactionDate", "CustomerId");

-- Inserção de dados na tabela Customers
INSERT INTO "Customers" ("Name", "Balance", "Limit")
VALUES
    ('o barato sai caro', 0, 1000 * 100),
    ('zan corp ltda', 0, 800 * 100),
    ('les cruders', 0, 10000 * 100),
    ('padaria joia de cocaia', 0, 100000 * 100),
    ('kid mais', 0, 5000 * 100);

