1. Preprocessing:
    - Grayscale
    - Noise reduction with blur filter
    - Automatic contrast adjustment
    - Edge detection
2. Features to recognise
    - Outer circle
        - Hough transform for ellipses
        - Low contrast compared to inner circles, may present issue. 
    - Outer boxy shape thing
    - Inner circles
        - Hough transform for ellipses 
        - Partially obscurred
        - Check they're the in the right position using outer circle as guideline
    - Possible 3rd prong 
    - Outer square? 
        - Corner detection
    - How to differentiate from 