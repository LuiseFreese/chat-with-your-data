-- Northstar Chat with Data – governed star schema.
-- Isolated in schema [cwd] inside your Fabric Warehouse. Nothing in [dbo] is touched.
-- Fabric Warehouse T-SQL: no IDENTITY, constraints are NOT ENFORCED, no money type.

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'cwd') EXEC('CREATE SCHEMA cwd');
GO

IF OBJECT_ID('cwd.DimDate') IS NULL
CREATE TABLE cwd.DimDate (
    [Date]          date         NOT NULL,
    [Year]          int          NOT NULL,
    [Quarter]       int          NOT NULL,
    [Quarter Label] varchar(10)  NOT NULL,   -- 'Q3 2026'
    [Month]         int          NOT NULL,
    [Month Label]   varchar(10)  NOT NULL,   -- '2026-07'
    [Month Name]    varchar(20)  NOT NULL,
    [Year Month]    int          NOT NULL    -- 202607
);

IF OBJECT_ID('cwd.DimRegion') IS NULL
CREATE TABLE cwd.DimRegion (
    [RegionKey] int         NOT NULL,
    [Region]    varchar(30) NOT NULL
);

IF OBJECT_ID('cwd.DimBusinessUnit') IS NULL
CREATE TABLE cwd.DimBusinessUnit (
    [BusinessUnitKey] int         NOT NULL,
    [Business Unit]   varchar(50) NOT NULL
);

IF OBJECT_ID('cwd.DimCustomer') IS NULL
CREATE TABLE cwd.DimCustomer (
    [CustomerKey]   int          NOT NULL,
    [Customer Name] varchar(120) NOT NULL,
    [Segment]       varchar(30)  NOT NULL,
    [Region]        varchar(30)  NOT NULL   -- descriptive only; facts carry RegionKey
);

IF OBJECT_ID('cwd.DimProduct') IS NULL
CREATE TABLE cwd.DimProduct (
    [ProductKey]    int           NOT NULL,
    [Product Name]  varchar(120)  NOT NULL,
    [Category]      varchar(50)   NOT NULL,
    [Business Unit] varchar(50)   NOT NULL,
    [List Price]    decimal(18,2) NOT NULL
);

IF OBJECT_ID('cwd.FactSalesOrderLine') IS NULL
CREATE TABLE cwd.FactSalesOrderLine (
    [OrderLineKey]     int           NOT NULL,
    [Order Id]         int           NOT NULL,
    [Order Date]       date          NOT NULL,
    [CustomerKey]      int           NOT NULL,
    [ProductKey]       int           NOT NULL,
    [RegionKey]        int           NOT NULL,
    [BusinessUnitKey]  int           NOT NULL,
    [Quantity]         int           NOT NULL,
    [Gross Amount]     decimal(18,2) NOT NULL,
    [Discount Amount]  decimal(18,2) NOT NULL,
    [Order Status]     varchar(20)   NOT NULL
);

IF OBJECT_ID('cwd.FactReturn') IS NULL
CREATE TABLE cwd.FactReturn (
    [ReturnKey]       int           NOT NULL,
    [Order Id]        int           NOT NULL,
    [Return Date]     date          NOT NULL,
    [RegionKey]       int           NOT NULL,
    [BusinessUnitKey] int           NOT NULL,
    [Amount]          decimal(18,2) NOT NULL,
    [Reason]          varchar(100)  NOT NULL
);

IF OBJECT_ID('cwd.FactFinanceAdjustment') IS NULL
CREATE TABLE cwd.FactFinanceAdjustment (
    [AdjustmentKey]   int           NOT NULL,
    [Adjustment Date] date          NOT NULL,
    [RegionKey]       int           NOT NULL,
    [BusinessUnitKey] int           NOT NULL,
    [Amount]          decimal(18,2) NOT NULL,
    [Reason]          varchar(100)  NOT NULL,
    [Source]          varchar(20)   NOT NULL,   -- 'Pipeline' | 'Excel'
    [Approved By]     varchar(100)  NOT NULL,
    [Status]          varchar(20)   NOT NULL,   -- 'Approved' | 'Pending'
    [Comment]         varchar(400)  NOT NULL,
    [Loaded At]       datetime2(6)  NOT NULL
);

-- Drives semantic-model RLS: one row per (user, region). Workspace Admins/Members bypass RLS by design.
IF OBJECT_ID('cwd.RegionAccess') IS NULL
CREATE TABLE cwd.RegionAccess (
    [User Principal Name] varchar(200) NOT NULL,
    [Region]              varchar(30)  NOT NULL
);
