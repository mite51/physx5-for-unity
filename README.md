# PhysX 5 for Unity

A Unity plugin for using PhysX 5 to simulate FEM soft bodies and PBD cloth and fluid.

This is a basic implementation with many features missing, such as collision filtering and scene query.

## Installation

Download and unzip the main package from the releases section, and place it in the appropriate directory. Then, download the corresponding libraries from the releases section.

Place the libraries for Linux (`\*.so`) and Windows (`\*.dll`) under `Plugins/Linux/x86_64` and `Plugins/Windows/x86_64`, respectively.

Install the package in a Unity project by going to "Window – Package Manager – `+` – Add package from disk…"

## Getting Started

Please refer to the sample scenes, which can be imported from the package after installation.

## Citation

This package is part of the [CRESSim](https://tbs-ualberta.github.io/CRESSim/) project, which was mainly focused on developing simulated surgical task environments. If you find this package useful for your research, please cite the following paper:

```bibtex
@inproceedings{ou2024realistic,
  title={A Realistic Surgical Simulator for Non-Rigid and Contact-Rich Manipulation in Surgeries with the da Vinci Research Kit}, 
  author={Ou, Yafei and Zargarzadeh, Sadra and Sedighi, Paniz and Tavakoli, Mahdi},
  booktitle={2024 21st International Conference on Ubiquitous Robots (UR)}, 
  year={2024},
  pages={64-70}
}
```