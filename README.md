# CRR Internship - Modular Building Layout Generator

This project was developed during my internship as part of the CRR initiative. It leverages C# and the Revit API to streamline and automate the layout design process for modular and panelized buildings. The tool is intended to assist Building Engineering students and professionals in architecture, construction, and MEP engineering by reducing time spent on repetitive design tasks.

## 🔧 Technologies Used
- **C#**
- **Revit API**
- **Visual Studio**
- **Autodesk Revit**

## 🏗️ Features
- Automated generation of 3D layouts for modular and panelized buildings.
- Graph-based space planning using force-directed algorithms.
- Geometry manipulation and simulation tools to enhance design efficiency.
- Area assignment and adjacency configuration based on user-defined constraints.
- Compatibility with varying factory module sizes and orientations.

## 📊 Design Methodology
1. **Graph Construction**  
   - Spaces are represented as nodes.  
   - Adjacency and strength of relationships are modeled as edges.

2. **Force-Directed Layout Algorithm**  
   - Nodes attract and repel based on connection strength and proximity.  
   - Layout stabilizes through iterative simulation and damping forces.

3. **Modular Placement Logic**  
   - Discretizes required space based on module dimensions.  
   - Supports layout scenarios (N-S, E-W orientation) and trims to site boundaries.  
   - Prioritizes placement based on spatial hierarchy and architectural logic.

4. **Constraints Considered**  
   - Building codes  
   - Minimum space and adjacency requirements  
   - Factory constraints (module dimensions, transport limitations)  
   - Material and cost optimization goals (future enhancement)

## 🎯 Objective
To create a functional and adaptable layout generation tool that saves time and enhances precision in early-stage building design. Future iterations may include optimization for cost, carbon footprint, and material usage.

## 📸 Demo / Screenshots
*Coming Soon*

## 👨‍💻 Author
Geon Kim  
Computer Science & Building Engineering Enthusiast  
[GitHub](https://github.com/Geon405)

---

Let me know if you want to add diagrams, sample Revit project outputs, or polish it more!
