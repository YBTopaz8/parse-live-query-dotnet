## [2.0.0](https://github.com/YOUR-REPO/Parse-SDK-dotNET/compare/1.0.0...2.0.0) (2024-Dec-11)
## What's changed ?
### **New Features 🚀**

- **Introduced Full Support for LINQ**  
  - Querying Parse Live Data now integrates seamlessly with LINQ.  
  - Enables filtering, projection, and chaining operations directly on queryable objects.

- **Added Rx.NET Integration**  
  - Facilitates reactive programming patterns for Live Queries.  
  - Subscriptions now support LINQ-like operators for stream composition (e.g., filtering, transforming).

#### **Why LINQ Is Great:**
1. Offers a familiar and powerful querying syntax for .NET developers.  
2. Provides better readability and maintainability when working with complex queries.  

#### **Why Rx.NET Is Great:**
1. Reactive streams simplify handling of real-time data and events.  
2. Operators like `Where`, `Select`, and `Throttle` allow precise control over data streams.

---

### **Changes**
- **Removed Callbacks in Favor of Rx.NET:**  
  - Replaced the callback-based event handling model with a fully Rx.NET-powered design.
  - This change improves readability and consistency for handling Live Query events.

- **Replaced Asynchronous Tasks with Synchronous Methods:**  
  - Simplifies the usage of the SDK by reducing the complexity of interacting with core operations.

- **Added Support for .NET 5, 6, 7, and 8:**  
  - Extended compatibility to include all major versions of .NET from 5 to 9, along with .NET MAUI.

---

### **Breaking Changes**
- **Dropped Callback Support:**  
  - Applications using the callback-based model for Live Queries will need to migrate to the new Rx.NET-based design.

- **Modified Task-Based APIs to Synchronous APIs:**  
  - Adjust your codebase to use the new synchronous methods where applicable.

---

### **Previous Releases**

## [1.0.0](https://github.com/YOUR-REPO/Parse-SDK-dotNET/releases/tag/1.0.0) (2024-Dec-05)

### **Features**
- Introduced support for Parse Live Queries in .NET.  
- Targeted .NET 9+ and .NET MAUI.  
- Enabled real-time updates for Parse objects with minimal delay.

---

This changelog communicates **what's new**, **breaking changes**, and **compatibility updates** effectively, setting the stage for your users to upgrade smoothly!