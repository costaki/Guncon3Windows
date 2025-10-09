using System;

public static class Homography
{
    // Resuelve H tal que [x' y' 1]^T ~ H * [x y 1]^T para 4 correspondencias (DLT)
    public static double[] Solve((double X,double Y)[] src, (double X,double Y)[] dst)
    {
        if (src.Length != 4 || dst.Length != 4) throw new ArgumentException("Necesito 4 pares (esquinas).");
        // Construye A*h = b, con h de 8 params (h22=1)
        var A = new double[8,8];
        var b = new double[8];
        for (int i=0;i<4;i++)
        {
            double x=src[i].X, y=src[i].Y, X=dst[i].X, Y=dst[i].Y;
            int r = 2*i;
            A[r,0]=x; A[r,1]=y; A[r,2]=1; A[r,3]=0; A[r,4]=0; A[r,5]=0; A[r,6]=-x*X; A[r,7]=-y*X; b[r]=X;
            A[r+1,0]=0; A[r+1,1]=0; A[r+1,2]=0; A[r+1,3]=x; A[r+1,4]=y; A[r+1,5]=1; A[r+1,6]=-x*Y; A[r+1,7]=-y*Y; b[r+1]=Y;
        }
        var h8 = SolveLinear8(A,b);
        var H = new double[9];
        H[0]=h8[0]; H[1]=h8[1]; H[2]=h8[2];
        H[3]=h8[3]; H[4]=h8[4]; H[5]=h8[5];
        H[6]=h8[6]; H[7]=h8[7]; H[8]=1.0;
        return H;
    }

    public static (double X,double Y) Apply(double[] H, double x, double y)
    {
        double X = H[0]*x + H[1]*y + H[2];
        double Y = H[3]*x + H[4]*y + H[5];
        double Z = H[6]*x + H[7]*y + H[8];
        if (Math.Abs(Z) < 1e-9) Z = 1e-9;
        return (X/Z, Y/Z);
    }

    // Resolución de 8x8 por Gauss (simple y suficiente aquí)
    private static double[] SolveLinear8(double[,] A, double[] b)
    {
        int n=8;
        var M = new double[n,n+1];
        for(int i=0;i<n;i++){ for(int j=0;j<n;j++) M[i,j]=A[i,j]; M[i,n]=b[i]; }

        for(int col=0; col<n; col++)
        {
            // pivote
            int piv=col;
            for(int r=col+1;r<n;r++) if (Math.Abs(M[r,col])>Math.Abs(M[piv,col])) piv=r;
            if (Math.Abs(M[piv,col])<1e-12) throw new Exception("Sistema singular.");
            if (piv!=col) for(int c=col;c<=n;c++){ var tmp=M[col,c]; M[col,c]=M[piv,c]; M[piv,c]=tmp; }

            // normalizar fila
            double f = M[col,col];
            for(int c=col;c<=n;c++) M[col,c]/=f;

            // eliminar resto
            for(int r=0;r<n;r++)
            {
                if (r==col) continue;
                double m = M[r,col];
                for(int c=col;c<=n;c++) M[r,c]-=m*M[col,c];
            }
        }

        var x = new double[n];
        for(int i=0;i<n;i++) x[i]=M[i,n];
        return x;
    }
}
