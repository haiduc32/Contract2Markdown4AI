# Requirements

This file contains requiremetns for the Contract2Markdown4AI project.

## Contract to Markdown

- The console app should take a parameter with the path to the openapi file (either.json or .yaml/yml).
- The application should transform the contract into individual markdown files per operation.
- output folder should be configurable. implicit will be ./output_md

### Markdown structure

Example structure:

```Markdown
# <API Name> <endpoint name>

<Endpoint Friendly Name> <operationId> <GET/POST..> </path/...>d

<description>

## Parameters

<the list of parameters: header/query>

## Request body

<description>

<model reference to first model>

## Response

### 200

<description>

<model description>

### 400

## Models

### <first model>

```
<model definition>
```

...

```

### Model documentation

- add model descriptions to the operation files. List all models that are referenced from one another recursively.
- list each used model only once. avoid getting into an infinite recursive loop.
- the models should be described using JSON Schema

## Libraries

- use NSwag

## Program.cs

This file should only contain at most CLI logic.

## Example imput

- use the file from ./ExampleContracts/Public-API.v1.json for a complex example contract

## Checking results

- generate the output into the ./output_check folder
  