import { HTTP_INTERCEPTORS } from '@angular/common/http';
import { APP_INITIALIZER, NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { AuthService } from '@signature/webfrontauth';
import { AppComponent } from './app.component';
import { NgxAuthModule, initializeAuthFactory, AuthInterceptor } from 'projects/webfrontauth-ngx/src/public-api';

@NgModule({
    declarations: [
      AppComponent
    ],
  imports: [
    BrowserModule,
    NgxAuthModule.forRoot()
  ],
  providers:
    [
    {
        // Refreshes authentication on startup
        provide: APP_INITIALIZER,
      useFactory: initializeAuthFactory,
      multi: true,
      deps: [AuthService]
    },
    {
        // Authenticates all HTTP requests made by Angular
        provide: HTTP_INTERCEPTORS,
      useClass: AuthInterceptor,
      multi: true
    }
  ],
  bootstrap: [AppComponent]
})
export class AppModule { }
