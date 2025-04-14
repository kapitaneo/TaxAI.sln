# TaxAI.sln

# 🧾 TaxLogicEngine

**TaxLogicEngine** is a .NET class library that provides core business logic for an AI-powered Canadian tax filing assistant.

It handles parsing of standardized CRA forms (such as T4), mapping data fields to CRA line numbers, generating draft tax returns, and supporting validation and AI-driven explanations.

---

## 📦 Features

- ✅ T4/T5 form models and DTOs
- ✅ Field mapping: Box → CRA Line (via JSON configuration)
- ✅ Draft tax return builder (to JSON or PDF)
- ✅ Rule-based validation engine (CRA limits, deductions)
- ✅ GPT assistant integration (explains what's being filed)
- ✅ Modular and testable architecture

---

## 📁 Project Structure

```plaintext
TaxLogicEngine/
├── Models/             # T4Slip, TaxReturn, TaxLineEntry
├── Mappings/           # JSON mappings (e.g., T4FieldMapping.json)
├── Services/           # Mapper, Calculator, Validator
├── Prompts/            # GPT prompt templates
├── Tests/              # Unit tests
└── TaxLogicEngine.csproj
