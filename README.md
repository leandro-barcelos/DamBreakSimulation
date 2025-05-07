<p align="center"><img src="https://socialify.git.ci/leandro-barcelos/SPH-TailingsDamFailure/image?font=Inter&amp;language=1&amp;name=1&amp;owner=1&amp;pattern=Solid&amp;theme=Dark" alt="project-image"></p>

## Abstract

This repository presents a Smoothed Particle Hydrodynamics (SPH) computational framework for simulating non-Newtonian tailings flows resulting from dam failures. The implementation employs GPU acceleration through compute shaders to efficiently simulate large-scale catastrophic events. The model incorporates complex terrain representation using SRTM elevation data and implements the momentum method for boundary treatment. The constitutive behavior of tailings is represented using a regularized Bingham-plastic model with parameters calibrated against laboratory data. 

The numerical framework has been validated against two catastrophic tailings dam failures: the 2015 Fundão Dam failure in Mariana and the 2019 Córrego do Feijão Dam I failure in Brumadinho, both in Minas Gerais, Brazil. Simulations accurately reproduce the affected areas within 11.07% error for Mariana and 3.12% error for Brumadinho cases, demonstrating the model's suitability for risk assessment applications.

## Research Objectives

This numerical framework addresses several research challenges:

1. Accurate representation of non-Newtonian rheological behavior of mining tailings
2. Efficient treatment of complex topographical boundaries
3. High-performance computation of large-scale simulations
4. Validation against real-world catastrophic events

## Methodology

### SPH Formulation

The model employs a Lagrangian meshless approach where fluid properties at any point are calculated as weighted contributions from neighboring particles using kernel functions. The mathematical formulation includes:

- Conservation equations discretized using the SPH method
- Regularized Bingham plastic model for non-Newtonian fluid behavior
- Momentum-based boundary treatment for complex topography
- Bucket-based linked-cell algorithm for efficient neighborhood searches

### Implementation Framework

The computational implementation utilizes compute shaders to leverage GPU parallelism, with:
- Particles represented as texels in 2D textures
- Multi-stage computation pipeline for density, force calculation, and advection
- Real terrain integration through SRTM digital elevation models

## Results

Quantitative comparison between simulation results and field measurements:

| Parameter | Mariana (Real) | Mariana (Sim) | Error (%) | Brumadinho (Real) | Brumadinho (Sim) | Error (%) |
|-----------|---------------|---------------|-------|-------------------|------------------|-------|
| Maximum Extension (km) | 10.34 | 8.86 | -14.33 | 8.58 | 7.10 | -17.19 |
| Affected Area (km²) | 3.56 | 3.17 | -11.07 | 2.54 | 2.46 | -3.12 |

Velocity profiles and spatiotemporal evolution of the tailings flow are available in the `/results/` directory.

### Visual Comparisons

![Mariana Simulation](./results/images/mariana_comparison.png)
*Figure 1: Comparison between simulated area (red) and actual affected area (blue) for the Mariana case.*

![Brumadinho Simulation](./results/images/brumadinho_comparison.png)
*Figure 2: Comparison between simulated area (red) and actual affected area (blue) for the Brumadinho case.*

## Limitations and Future Work

Current limitations include:
- Memory constraints limiting particle resolution
- Under-prediction of maximum flow extent

Future development directions:
- Implementation of a compressed neighborhood data structure
- Integration of higher-resolution terrain models
