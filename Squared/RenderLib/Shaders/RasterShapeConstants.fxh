// HACK suggested by Sean Barrett: Increase all line widths to ensure that a diagonal 1px-thick line covers one pixel
#define OutlineSizeCompensation 2.1

#define PI 3.1415926535897931
#define DEG_TO_RAD (PI / 180.0)

#define TYPE_Ellipse 0
#define TYPE_LineSegment 1
#define TYPE_Rectangle 2
#define TYPE_Triangle 3
#define TYPE_QuadraticBezier 4
#define TYPE_Arc 5
#define TYPE_Polygon 6

// Generic gradient types that operate on the bounding box or something
//  similar to it
#define GRADIENT_TYPE_Natural 0
#define GRADIENT_TYPE_Linear 1
#define GRADIENT_TYPE_Linear_Enclosing 2
#define GRADIENT_TYPE_Linear_Enclosed 3
#define GRADIENT_TYPE_Radial 4
#define GRADIENT_TYPE_Radial_Enclosing 5
#define GRADIENT_TYPE_Radial_Enclosed 6
#define GRADIENT_TYPE_Along 7
// The gradient weight has already been computed by the evaluate function
#define GRADIENT_TYPE_Other 8
// Generic gradient that operates on the bounding box, with the angle added
//  to the base
#define GRADIENT_TYPE_Angular 512
#define GRADIENT_TYPE_Conical (GRADIENT_TYPE_Angular + 720)