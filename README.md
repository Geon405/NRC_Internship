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
<img width="928" alt="Screenshot 2025-03-24 at 10 28 22 PM" src="https://github.com/user-attachments/assets/aa6ed7e9-ce08-4f36-abe5-e18baf835d0f" />
<img width="787" alt="Screenshot 2025-03-24 at 10 29 00 PM" src="https://github.com/user-attachments/assets/a7ff719c-b8bb-4e58-9387-fd9763810009" />
<img width="943" alt="Screenshot 2025-03-24 at 10 29 15 PM" src="https://github.com/user-attachments/assets/84a74d15-39c9-4468-b2de-107b495fbe60" />
<img width="907" alt="Screenshot 2025-03-24 at 10 29 37 PM" src="https://github.com/user-attachments/assets/3f235048-8820-4114-9f5f-766970d20c64" />



